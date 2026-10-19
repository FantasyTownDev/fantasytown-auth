namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 管理域越权校验（V5 §7.2 冻结语义，Domain 层唯一实现点）
/// 规则一：目标防护——操作者权限必须严格高于目标（全系统唯一允许比较权限大小之处）
/// 规则二：禁止自我操作（防自我封禁/自我降权导致站点失控）
/// 唯一例外：解除 LOCKOUT 走专用判定 <see cref="CanClearLockout"/>（V5 评审 N1）
/// </summary>
public static class ManagementGuard
{
    /// <summary>
    /// 规则一：操作者权限严格高于目标才可管理（同级/向上一律拒绝）
    /// </summary>
    public static bool CanManage(UserPermission actor, UserPermission target) => (byte)actor > (byte)target;

    /// <summary>
    /// 规则二：禁止自我操作
    /// </summary>
    public static bool NotSelf(int actorUid, int targetUid) => actorUid != targetUid;

    /// <summary>
    /// 解除 LOCKOUT 专用判定：自我解锁放行（非降权类操作，唯一服主被锁后的自救通道）；
    /// 他人须满足 <see cref="CanManage"/>（协管不能解服主的锁）
    /// </summary>
    public static bool CanClearLockout(UserPermission actor, UserPermission target, int actorUid, int targetUid) =>
        actorUid == targetUid || CanManage(actor, target);
}
