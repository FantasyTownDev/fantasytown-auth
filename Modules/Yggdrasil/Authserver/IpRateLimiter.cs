using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// IP 固定窗限流器
/// Redis 键：FAIL_IP:{ip}
/// 窗口：1 分钟，超过阈值拒绝
/// </summary>
public sealed class IpRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly int _database;

    /// <summary>
    /// INCR + 首次设置过期，单脚本原子执行（审计 M2）：
    /// 修复 INCR/EXPIRE 两次往返间的丢失窗口——首个请求若中断，键将永不过期，IP 被永久限死
    /// </summary>
    private static readonly LuaScript IncrementWindowScript = LuaScript.Prepare(@"
local c = redis.call('INCR', @redisKey)
if c == 1 then
    redis.call('EXPIRE', @redisKey, @windowSeconds)
end
return c
");

    public IpRateLimiter(IConnectionMultiplexer redis, int limit = 30, int database = -1)
    {
        _redis = redis;
        _limit = limit;
        _window = TimeSpan.FromMinutes(1);
        _database = database;
    }

    public async ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var redisKey = $"FAIL_IP:{key}";

        var raw = await db.ScriptEvaluateAsync(IncrementWindowScript, new
        {
            redisKey = (RedisKey)redisKey,
            windowSeconds = (RedisValue)(long)_window.TotalSeconds
        });
        var count = (long)raw!;

        return count <= _limit;
    }

    public async ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var redisKey = $"FAIL_IP:{key}";
        var value = await db.StringGetAsync(redisKey);
        return value.HasValue ? (int)value : 0;
    }
}
