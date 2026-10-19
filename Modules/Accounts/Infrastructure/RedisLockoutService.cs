using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis 锁定服务实现（V5 §6.6 冻结键空间）
/// LOCKOUT:{uid}     — 账户锁定（count=触发次数，TTL=锁定时长；跨通道共享）
/// FAIL_EP:{email}:{ip} — 同 IP 复合键 ZSET 滑动窗 15min（LOCKOUT 触发依据，阈值 5）
/// FAIL_ACCT:{email} — 账户全局 ZSET 滑动窗 15min（仅审计/告警，不触发锁定）
/// TODO(Step 0-c)：ZADD/EXPIRE/INCR 多命令改为 Lua 原子执行（审计 H4）
/// </summary>
public sealed class RedisLockoutService : ILockoutService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockoutDuration;
    private readonly int _database;

    public RedisLockoutService(
        IConnectionMultiplexer redis,
        int maxAttempts = 5,
        int windowMinutes = 15,
        int lockoutMinutes = 15,
        int database = -1)
    {
        _redis = redis;
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
        _lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        _database = database;
    }

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

        // FAIL_EP:{email}:{ip} 同 IP 复合键滑动窗
        var epKey = $"FAIL_EP:{email}:{ip}";
        await db.SortedSetAddAsync(epKey, $"{now}:{Guid.NewGuid():N}", now);
        await db.SortedSetRemoveRangeByScoreAsync(epKey, 0, windowStart);
        await db.KeyExpireAsync(epKey, _window);

        // FAIL_ACCT:{email} 全局审计维度（不触发锁定）
        var acctKey = $"FAIL_ACCT:{email}";
        await db.SortedSetAddAsync(acctKey, $"{now}:{Guid.NewGuid():N}", now);
        await db.SortedSetRemoveRangeByScoreAsync(acctKey, 0, windowStart);
        await db.KeyExpireAsync(acctKey, _window);

        var count = (int)await db.SortedSetLengthAsync(epKey);

        // 仅"同一 IP 对同一账户"窗口内 ≥5 触发 LOCKOUT:{uid}（跨 IP 分散失败无法锁死账户）
        var triggered = count >= _maxAttempts;
        if (triggered)
        {
            var lockoutKey = LockoutKey(uid);
            await db.StringIncrementAsync(lockoutKey);
            await db.KeyExpireAsync(lockoutKey, _lockoutDuration);
        }

        return new LockoutFailureResult(count, triggered);
    }

    public async Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        // 未知账户：仅 FAIL_ACCT 审计维度，永不设置 LOCKOUT（无 uid 可锁）
        var acctKey = $"FAIL_ACCT:{identifier}";
        await db.SortedSetAddAsync(acctKey, $"{now}:{Guid.NewGuid():N}", now);
        await db.SortedSetRemoveRangeByScoreAsync(acctKey, 0, windowStart);
        await db.KeyExpireAsync(acctKey, _window);
    }

    public async Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        await db.KeyDeleteAsync(LockoutKey(uid));
        await db.KeyDeleteAsync($"FAIL_EP:{email}:{ip}");
        // FAIL_ACCT:{email} 仅审计，由滑动窗自然过期
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

    public InMemoryLockoutService(
        int maxAttempts = 5,
        int windowMinutes = 15,
        int lockoutMinutes = 15)
    {
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
        _lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
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
            _lockouts[uid] = (nextCount, DateTime.UtcNow.Add(_lockoutDuration));
        }

        return Task.FromResult(new LockoutFailureResult(epCount, triggered));
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
