namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 限流器接口
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// 检查是否允许请求。允许时自动计数。
    /// </summary>
    ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前窗口内的请求计数。
    /// </summary>
    ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default);
}
