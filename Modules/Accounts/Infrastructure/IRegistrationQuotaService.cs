namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 注册配额服务（V5 §3.4/§4.1 冻结语义）
/// REG_IP:{ip} — count，TTL 1hour；前置 GET 粗检 + 仅注册成功后 INCR（软配额）
/// </summary>
public interface IRegistrationQuotaService
{
    /// <summary>
    /// 粗检：该 IP 当前小时窗口内是否仍可注册（只读 GET，不消耗配额）
    /// </summary>
    Task<bool> CanRegisterAsync(string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册成功后计数（INCR，失败请求不消耗配额）。返回新计数；
    /// 超过配额即写 register-quota-exceeded 告警（软配额，由运维处置，§4.1）
    /// </summary>
    Task<long> RecordRegistrationAsync(string ip, CancellationToken cancellationToken = default);
}
