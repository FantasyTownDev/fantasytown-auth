using System.Collections.Concurrent;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 内存限流器（用于单元测试）
/// </summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();
    private readonly int _limit;
    private readonly TimeSpan _window;

    public InMemoryRateLimiter(int limit = 30, TimeSpan? window = null)
    {
        _limit = limit;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var records = _requests.GetOrAdd(key, _ => new List<DateTime>());

        lock (records)
        {
            records.RemoveAll(r => now - r > _window);

            if (records.Count >= _limit)
            {
                return ValueTask.FromResult(false);
            }

            records.Add(now);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (_requests.TryGetValue(key, out var records))
        {
            lock (records)
            {
                records.RemoveAll(r => now - r > _window);
                return ValueTask.FromResult(records.Count);
            }
        }

        return ValueTask.FromResult(0);
    }
}
