namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 账户锁定服务接口（V5 §6.6 冻结语义）
/// LOCKOUT:{uid} 由 Web 登录与 Ygg authenticate 共享同一键空间；
/// 仅"同一 IP 对同一账户"（FAIL_EP:{email}:{ip} 窗口内 ≥5）触发锁定；
/// 跨 IP 分散失败只计入 FAIL_ACCT:{email}（审计维度，不触发锁定）
/// </summary>
public interface ILockoutService
{
    /// <summary>
    /// 检查账户是否被锁定（LOCKOUT:{uid}，跨通道共享）
    /// </summary>
    Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取剩余锁定时间（秒）；null 表示未锁定
    /// </summary>
    Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录已存在账户的一次认证失败：
    /// FAIL_EP:{email}:{ip} 同 IP 复合键滑动窗 + FAIL_ACCT:{email} 审计维度；
    /// 同 IP 窗口内达到阈值时触发 LOCKOUT:{uid}（退避升级见 Step 0-c）
    /// </summary>
    Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录未知账户的一次失败尝试：仅计入 FAIL_ACCT:{identifier}（审计/告警，永不锁定）
    /// </summary>
    Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登录成功：清除 LOCKOUT:{uid} 与 FAIL_EP:{email}:{ip}
    /// （FAIL_ACCT:{email} 仅审计，由窗口自然过期）
    /// </summary>
    Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default);
}

/// <summary>
/// 一次失败记录的结果
/// </summary>
/// <param name="FailureCount">FAIL_EP:{email}:{ip} 窗口内失败次数</param>
/// <param name="LockoutTriggered">本次失败是否触发了 LOCKOUT:{uid}</param>
public sealed record LockoutFailureResult(int FailureCount, bool LockoutTriggered);
