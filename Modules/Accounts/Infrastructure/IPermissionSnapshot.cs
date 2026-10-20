using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 权限快照接口 - 用于快速判断用户权限
/// </summary>
public interface IPermissionSnapshot
{
    /// <summary>
    /// 获取用户权限快照
    /// </summary>
    /// <param name="uid">用户 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>权限快照。实现保证回源空结果返回封禁等效哨兵（IsBanned=true、BannedUntil=null，V5 评审 N2）；
    /// null 仅作防御性残留值，调用方一律按封禁等效拒绝处理（审计 C3）。</returns>
    Task<PermissionSnapshot?> GetAsync(int uid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置用户权限快照
    /// </summary>
    Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除用户权限快照
    /// </summary>
    Task RemoveAsync(int uid, CancellationToken cancellationToken = default);
}

/// <summary>
/// 权限快照
/// </summary>
public sealed record PermissionSnapshot
{
    /// <summary>
    /// 用户权限
    /// </summary>
    public UserPermission Permission { get; init; }

    /// <summary>
    /// 是否被封禁
    /// </summary>
    public bool IsBanned { get; init; }

    /// <summary>
    /// 封禁截止时间（null 表示永久封禁）
    /// </summary>
    public DateTime? BannedUntil { get; init; }

    /// <summary>
    /// 封禁原因
    /// </summary>
    public string? BannedReason { get; init; }

    /// <summary>
    /// 快照创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
