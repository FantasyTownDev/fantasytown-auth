using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis 锁定服务实现
/// 使用 ZSET 实现滑动窗口限流
/// 键：LOCKOUT:{identifier}
/// 值：失败时间戳（score）
/// TTL：15 分钟（窗口大小）
/// 阈值：5 次失败触发锁定
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

    public async Task<int> RecordFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"LOCKOUT:{identifier}";
        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 添加失败记录（member 用唯一标识，score 用时间戳）
        await db.SortedSetAddAsync(key, $"{now.Ticks}:{Guid.NewGuid():N}", now.Ticks);

        // 清理过期记录
        await db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart.Ticks);

        // 设置键过期
        await db.KeyExpireAsync(key, _window);

        // 返回当前窗口内的失败次数
        var count = await db.SortedSetLengthAsync(key);
        return (int)count;
    }

    public async Task<bool> IsLockedOutAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"LOCKOUT:{identifier}";
        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 清理过期记录
        await db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart.Ticks);

        // 检查失败次数
        var count = await db.SortedSetLengthAsync(key);
        return count >= _maxAttempts;
    }

    public async Task<double?> GetRemainingLockoutSecondsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"LOCKOUT:{identifier}";
        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 清理过期记录
        await db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart.Ticks);

        // 检查失败次数
        var count = await db.SortedSetLengthAsync(key);
        if (count < _maxAttempts)
            return null;

        // 获取最早的失败记录
        var entries = await db.SortedSetRangeByScoreWithScoresAsync(key, windowStart.Ticks, now.Ticks, Exclude.None, Order.Ascending, 0, 1);
        if (entries.Length == 0)
            return null;

        var earliestFailure = new DateTime((long)entries[0].Score, DateTimeKind.Utc);
        var lockoutEnd = earliestFailure + _lockoutDuration;

        if (lockoutEnd <= now)
            return 0;

        return (lockoutEnd - now).TotalSeconds;
    }

    public async Task ResetAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"LOCKOUT:{identifier}";
        await db.KeyDeleteAsync(key);
    }
}

/// <summary>
/// 内存锁定服务实现（用于单元测试）
/// </summary>
public sealed class InMemoryLockoutService : ILockoutService
{
    private readonly Dictionary<string, List<DateTime>> _failures = new();
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

    public Task<int> RecordFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (!_failures.ContainsKey(identifier))
            _failures[identifier] = new List<DateTime>();

        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 清理过期记录
        _failures[identifier].RemoveAll(t => t < windowStart);

        // 添加新记录
        _failures[identifier].Add(now);

        return Task.FromResult(_failures[identifier].Count);
    }

    public Task<bool> IsLockedOutAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (!_failures.ContainsKey(identifier))
            return Task.FromResult(false);

        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 清理过期记录
        _failures[identifier].RemoveAll(t => t < windowStart);

        return Task.FromResult(_failures[identifier].Count >= _maxAttempts);
    }

    public Task<double?> GetRemainingLockoutSecondsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (!_failures.ContainsKey(identifier))
            return Task.FromResult<double?>(null);

        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // 清理过期记录
        _failures[identifier].RemoveAll(t => t < windowStart);

        if (_failures[identifier].Count < _maxAttempts)
            return Task.FromResult<double?>(null);

        var earliestFailure = _failures[identifier].Min();
        var lockoutEnd = earliestFailure + _lockoutDuration;

        if (lockoutEnd <= now)
            return Task.FromResult<double?>(0);

        return Task.FromResult<double?>((lockoutEnd - now).TotalSeconds);
    }

    public Task ResetAsync(string identifier, CancellationToken cancellationToken = default)
    {
        _failures.Remove(identifier);
        return Task.CompletedTask;
    }
}
