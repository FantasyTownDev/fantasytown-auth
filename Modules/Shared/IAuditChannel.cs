namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 异步审计事件通道接口。
/// 热路径仅投递 IAuditEvent 到 Channel，由 BackgroundService 攒批批量落库 auth_log。
/// </summary>
public interface IAuditChannel
{
    /// <summary>
    /// 入队审计事件，非阻塞 fire-and-forget 语义。
    /// </summary>
    ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// 审计事件模型，1:1 映射 auth_log 表字段。
/// </summary>
public sealed record AuditEvent
{
    /// <summary>事件类型（action 列）：login-success, register, ban 等</summary>
    public required string Action { get; init; }

    /// <summary>执行者 UID（系统事件如 bootstrap 为 null）</summary>
    public int? ActorId { get; init; }

    /// <summary>
    /// 主体 ID：指向 User (uid) 或 Player (pid)，取决于 action 类型。
    /// 具体实体存储在 Context 字段中。
    /// </summary>
    public required int SubjectId { get; init; }

    /// <summary>JSON 详情快照（旧/新值、IP、原因、实体类型）</summary>
    public string? Context { get; init; }

    /// <summary>真实客户端 IP（PII，受保留策略约束）</summary>
    public string? Ip { get; init; }

    /// <summary>事件 UTC 时间戳</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
