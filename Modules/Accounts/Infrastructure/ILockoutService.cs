namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 账户锁定服务接口
/// 用于跟踪登录失败次数，达到阈值后锁定账户
/// </summary>
public interface ILockoutService
{
    /// <summary>
    /// 记录登录失败
    /// </summary>
    /// <param name="identifier">标识（IP 或 用户ID）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前失败次数</returns>
    Task<int> RecordFailureAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否被锁定
    /// </summary>
    /// <param name="identifier">标识（IP 或 用户ID）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否被锁定</returns>
    Task<bool> IsLockedOutAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取剩余锁定时间（秒）
    /// </summary>
    /// <param name="identifier">标识（IP 或 用户ID）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剩余秒数，null 表示未锁定</returns>
    Task<double?> GetRemainingLockoutSecondsAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置锁定状态
    /// </summary>
    /// <param name="identifier">标识（IP 或 用户ID）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ResetAsync(string identifier, CancellationToken cancellationToken = default);
}
