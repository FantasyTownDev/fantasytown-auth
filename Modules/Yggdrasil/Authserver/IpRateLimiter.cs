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

        var count = await db.StringIncrementAsync(redisKey);
        if (count == 1)
        {
            await db.KeyExpireAsync(redisKey, _window);
        }

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
