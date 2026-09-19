namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 认证并发许可限流器
/// 全局并发限制 ≈ 2×CPU 核心数，QueueLimit=0，超限 403
/// </summary>
public sealed class AuthConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _queueLimit;

    public AuthConcurrencyLimiter(int maxConcurrency = 0, int queueLimit = 0)
    {
        if (maxConcurrency <= 0)
        {
            maxConcurrency = Environment.ProcessorCount * 2;
        }

        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _queueLimit = queueLimit;
        MaxConcurrency = maxConcurrency;
    }

    public int MaxConcurrency { get; }
    public int CurrentCount => _semaphore.CurrentCount;

    public bool TryAcquire()
    {
        if (_queueLimit == 0 && _semaphore.CurrentCount == 0)
        {
            return false;
        }

        return _semaphore.Wait(0);
    }

    public void Release()
    {
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
