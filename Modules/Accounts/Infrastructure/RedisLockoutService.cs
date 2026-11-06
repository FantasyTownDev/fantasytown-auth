// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis 锁定服务实现（V5 §6.6 冻结键空间）
/// LOCKOUT:{uid}     — 账户锁定（count=触发次数，TTL=指数退避 15→30→60min 封顶；跨通道共享）
/// FAIL_EP:{email}:{ip} — 同 IP 复合键 ZSET 滑动窗 15min（LOCKOUT 触发依据，阈值 5）
/// FAIL_ACCT:{email} — 账户全局 ZSET 滑动窗 15min（仅审计/告警，不触发锁定）
/// H4：记录失败的"写入/修剪/过期/阈值判定/锁定"由单 Lua 脚本原子执行
/// H3：仅精确键操作（无 IP 前缀清空），计数经滑动窗修剪 + 键 TTL 自然过期
/// </summary>
public sealed class RedisLockoutService : ILockoutService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockoutDuration;
    private readonly int _database;
    private readonly AuthDbContext? _auditDb;

    public RedisLockoutService(
        IConnectionMultiplexer redis,
        int maxAttempts = 5,
        int windowMinutes = 15,
        int lockoutMinutes = 15,
        int database = -1,
        AuthDbContext? auditDb = null)
    {
        _redis = redis;
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
        _lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        _database = database;
        _auditDb = auditDb;
    }

    /// <summary>
    /// 记录一次已知账户失败（审计 H4 原子化）：
    /// 双滑动窗写入/修剪/TTL + FAIL_EP 阈值判定 + LOCKOUT 触发，单脚本执行
    /// </summary>
    private static readonly LuaScript RecordFailureScript = LuaScript.Prepare(@"
redis.call('ZADD', @epKey, @now, @epMember)
redis.call('ZREMRANGEBYSCORE', @epKey, 0, @windowStart)
redis.call('EXPIRE', @epKey, @windowSeconds)
redis.call('ZADD', @acctKey, @now, @acctMember)
redis.call('ZREMRANGEBYSCORE', @acctKey, 0, @windowStart)
redis.call('EXPIRE', @acctKey, @windowSeconds)
local count = redis.call('ZCARD', @epKey)
local triggered = 0
if count >= tonumber(@maxAttempts) then
    local n = redis.call('INCR', @lockoutKey)
    local ttl = tonumber(@backoffBaseSeconds) * 2 ^ (n - 1)
    if ttl > tonumber(@backoffCapSeconds) then ttl = tonumber(@backoffCapSeconds) end
    redis.call('EXPIRE', @lockoutKey, ttl)
    triggered = 1
end
return {count, triggered}
");

    /// <summary>
    /// 记录一次未知账户失败（仅 FAIL_ACCT 审计维度，单脚本原子执行）
    /// </summary>
    private static readonly LuaScript RecordUnknownFailureScript = LuaScript.Prepare(@"
redis.call('ZADD', @acctKey, @now, @member)
redis.call('ZREMRANGEBYSCORE', @acctKey, 0, @windowStart)
redis.call('EXPIRE', @acctKey, @windowSeconds)
return 1
");

    public async Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        return await db.KeyExistsAsync(LockoutKey(uid));
    }

    public async Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var ttl = await db.KeyTimeToLiveAsync(LockoutKey(uid));
        return ttl?.TotalSeconds;
    }

    public async Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        var raw = await db.ScriptEvaluateAsync(RecordFailureScript, new
        {
            epKey = (RedisKey)$"FAIL_EP:{email}:{ip}",
            acctKey = (RedisKey)$"FAIL_ACCT:{email}",
            lockoutKey = (RedisKey)LockoutKey(uid),
            now = (RedisValue)now,
            windowStart = (RedisValue)windowStart,
            epMember = (RedisValue)$"{now}:{Guid.NewGuid():N}",
            acctMember = (RedisValue)$"{now}:{Guid.NewGuid():N}",
            windowSeconds = (RedisValue)(long)_window.TotalSeconds,
            maxAttempts = (RedisValue)_maxAttempts,
            backoffBaseSeconds = (RedisValue)(long)_lockoutDuration.TotalSeconds,
            backoffCapSeconds = (RedisValue)(long)(_lockoutDuration.TotalSeconds * 4)
        });
        // 脚本契约：恒返回 {count, triggered}
        var result = (RedisResult[])raw!;
        var count = (int)result[0];
        var triggered = (int)result[1] == 1;

        // V5 §6.6：锁定触发写 lockout 审计（受害账户/来源 IP/计数），供 Lockouts 派生视图
        // 与定向攻击识别；触发后每次阈值内失败都重新触发（INCR 续期），逐次落审计
        if (triggered && _auditDb != null)
        {
            _auditDb.AuthLogs.Add(new AuthLog
            {
                Action = "lockout",
                SubjectId = uid,
                Ip = ip,
                Context = $"count={count},email={email}",
                CreatedAt = DateTime.UtcNow
            });
            await _auditDb.SaveChangesAsync(cancellationToken);
        }

        return new LockoutFailureResult(count, triggered);
    }

    public async Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        // 未知账户：仅 FAIL_ACCT 审计维度，永不设置 LOCKOUT（无 uid 可锁）
        await db.ScriptEvaluateAsync(RecordUnknownFailureScript, new
        {
            acctKey = (RedisKey)$"FAIL_ACCT:{identifier}",
            now = (RedisValue)now,
            member = (RedisValue)$"{now}:{Guid.NewGuid():N}",
            windowStart = (RedisValue)windowStart,
            windowSeconds = (RedisValue)(long)_window.TotalSeconds
        });
    }

    public async Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        await db.KeyDeleteAsync(LockoutKey(uid));
        await db.KeyDeleteAsync($"FAIL_EP:{email}:{ip}");
        // FAIL_ACCT:{email} 仅审计，由滑动窗自然过期
    }

    public async Task<bool> ClearAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        // V5 §7.3：管理员清除仅删锁定键，失败计数窗保留（再犯提醒语境）
        return await db.KeyDeleteAsync(LockoutKey(uid));
    }

    private static string LockoutKey(int uid) => $"LOCKOUT:{uid}";
}

/// <summary>
/// 内存锁定服务实现（用于单元测试，镜像 Redis 实现的 §6.6 语义）
/// </summary>
public sealed class InMemoryLockoutService : ILockoutService
{
    private readonly Dictionary<string, List<long>> _windows = new();
    private readonly Dictionary<int, (int Count, DateTime ExpiresAt)> _lockouts = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockoutDuration;
    private readonly AuthDbContext? _auditDb;

    public InMemoryLockoutService(
        int maxAttempts = 5,
        int windowMinutes = 15,
        int lockoutMinutes = 15,
        AuthDbContext? auditDb = null)
    {
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
        _lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        _auditDb = auditDb;
    }

    public Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(uid, out var state))
        {
            if (state.ExpiresAt > DateTime.UtcNow)
                return Task.FromResult(true);
            _lockouts.Remove(uid);
        }
        return Task.FromResult(false);
    }

    public Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(uid, out var state))
        {
            var remaining = (state.ExpiresAt - DateTime.UtcNow).TotalSeconds;
            if (remaining > 0)
                return Task.FromResult<double?>(remaining);
            _lockouts.Remove(uid);
        }
        return Task.FromResult<double?>(null);
    }

    public Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        var epCount = AddWindowed($"FAIL_EP:{email}:{ip}", now, windowStart);
        AddWindowed($"FAIL_ACCT:{email}", now, windowStart);

        var triggered = epCount >= _maxAttempts;
        if (triggered)
        {
            var nextCount = _lockouts.TryGetValue(uid, out var prev) ? prev.Count + 1 : 1;
            _lockouts[uid] = (nextCount, DateTime.UtcNow.Add(Backoff(nextCount)));

            // 镜像 Redis 实现：锁定触发审计（V5 §6.6）
            _auditDb?.AuthLogs.Add(new AuthLog
            {
                Action = "lockout",
                SubjectId = uid,
                Ip = ip,
                Context = $"count={epCount},email={email}",
                CreatedAt = DateTime.UtcNow
            });
            _auditDb?.SaveChanges();
        }

        return Task.FromResult(new LockoutFailureResult(epCount, triggered));
    }

    /// <summary>T3-006 指数退避：base → 2base → 4base 封顶（默认 15→30→60min，§6.6/§8）</summary>
    private TimeSpan Backoff(int triggerCount)
    {
        var factor = Math.Min(Math.Pow(2, triggerCount - 1), 4);
        return TimeSpan.FromMinutes(_lockoutDuration.TotalMinutes * factor);
    }

    public Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;
        AddWindowed($"FAIL_ACCT:{identifier}", now, windowStart);
        return Task.CompletedTask;
    }

    public Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        _lockouts.Remove(uid);
        _windows.Remove($"FAIL_EP:{email}:{ip}");
        // FAIL_ACCT:{email} 仅审计，由窗口自然过期
        return Task.CompletedTask;
    }

    public Task<bool> ClearAsync(int uid, CancellationToken cancellationToken = default)
    {
        // 镜像 Redis 实现：仅清锁定，保留失败计数窗
        var existed = _lockouts.Remove(uid);
        return Task.FromResult(existed);
    }

    private int AddWindowed(string key, long now, long windowStart)
    {
        if (!_windows.TryGetValue(key, out var list))
        {
            list = new List<long>();
            _windows[key] = list;
        }
        list.RemoveAll(t => t < windowStart);
        list.Add(now);
        return list.Count;
    }
}
