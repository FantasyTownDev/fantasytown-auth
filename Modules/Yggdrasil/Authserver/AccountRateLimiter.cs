using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Account×IP 滑动窗限流器
/// Redis 键：FAIL_EP:{email}:{ip}
/// 机制：ZSET 滑动窗，score = 时间戳，member = 唯一 ID
/// 阈值：15 分钟内 5 次失败触发 LOCKOUT
/// </summary>
public sealed class AccountRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly int _database;

    public AccountRateLimiter(IConnectionMultiplexer redis, int limit = 5, int database = -1)
    {
        _redis = redis;
        _limit = limit;
        _window = TimeSpan.FromMinutes(15);
        _database = database;
    }

    public async ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var redisKey = $"FAIL_EP:{key}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        await db.SortedSetRemoveRangeByScoreAsync(redisKey, 0, windowStart);

        var member = $"{now}:{Guid.NewGuid():N}";
        await db.SortedSetAddAsync(redisKey, member, now);

        await db.KeyExpireAsync(redisKey, _window);

        var count = await db.SortedSetLengthAsync(redisKey);
        return count <= _limit;
    }

    public async ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var redisKey = $"FAIL_EP:{key}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        await db.SortedSetRemoveRangeByScoreAsync(redisKey, 0, windowStart);
        var count = await db.SortedSetLengthAsync(redisKey);
        return (int)count;
    }
}
