// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Shared;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis 注册配额服务（V5 §3.4：REG_IP:{ip} count，TTL 1hour）
/// </summary>
public sealed class RedisRegistrationQuotaService : IRegistrationQuotaService
{
    /// <summary>
    /// INCR + 首次设置过期，单脚本原子执行（与 FAIL_IP 同模式，防 EXPIRE 丢失）
    /// </summary>
    private static readonly LuaScript IncrementQuotaScript = LuaScript.Prepare(@"
local c = redis.call('INCR', @quotaKey)
if c == 1 then
    redis.call('EXPIRE', @quotaKey, @ttlSeconds)
end
return c
");

    private readonly IConnectionMultiplexer _redis;
    private readonly SiteOptions _siteOptions;
    private readonly ILogger? _logger;
    private readonly int _database;

    public RedisRegistrationQuotaService(
        IConnectionMultiplexer redis,
        SiteOptions siteOptions,
        ILogger? logger = null,
        int database = -1)
    {
        _redis = redis;
        _siteOptions = siteOptions;
        _logger = logger;
        _database = database;
    }

    public async Task<bool> CanRegisterAsync(string ip, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var used = await db.StringGetAsync(RegKey(ip));
        return !used.HasValue || (long)used < _siteOptions.RegsPerIp;
    }

    public async Task<long> RecordRegistrationAsync(string ip, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var raw = await db.ScriptEvaluateAsync(IncrementQuotaScript, new
        {
            quotaKey = (RedisKey)RegKey(ip),
            ttlSeconds = (RedisValue)(long)TimeSpan.FromHours(1).TotalSeconds
        });
        var count = (long)raw!;

        if (count > _siteOptions.RegsPerIp)
        {
            // §4.1 软配额：并发越过粗检的超发写告警（auth_log 审计通道随 P4 装配，事件名先固化在日志）
            _logger?.LogWarning(
                "register-quota-exceeded: {Ip} reached {Count} registrations (limit {Limit})",
                ip, count, _siteOptions.RegsPerIp);
        }

        return count;
    }

    private static string RegKey(string ip) => $"REG_IP:{ip}";
}

/// <summary>
/// 内存注册配额服务（单元测试）：limit 为 null 时不限额
/// </summary>
public sealed class InMemoryRegistrationQuotaService : IRegistrationQuotaService
{
    private readonly Dictionary<string, long> _counts = new();
    private readonly long? _limit;
    private readonly ILogger? _logger;

    public InMemoryRegistrationQuotaService(long? limit = null, ILogger? logger = null)
    {
        _limit = limit;
        _logger = logger;
    }

    public Task<bool> CanRegisterAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (_limit is null)
        {
            return Task.FromResult(true);
        }
        return Task.FromResult(!_counts.TryGetValue(ip, out var used) || used < _limit);
    }

    public Task<long> RecordRegistrationAsync(string ip, CancellationToken cancellationToken = default)
    {
        _counts.TryGetValue(ip, out var used);
        var count = used + 1;
        _counts[ip] = count;

        if (_limit is long limit && count > limit)
        {
            _logger?.LogWarning(
                "register-quota-exceeded: {Ip} reached {Count} registrations (limit {Limit})",
                ip, count, limit);
        }

        return Task.FromResult(count);
    }
}
