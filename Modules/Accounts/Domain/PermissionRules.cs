namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 权限判定规则 - 白名单精确匹配
/// </summary>
public static class PermissionRules
{
    /// <summary>
    /// 是否为服主或管理员（协管及以上）
    /// </summary>
    public static bool IsModerator(UserPermission role) =>
        role is UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin;

    /// <summary>
    /// 是否为服主
    /// </summary>
    public static bool IsOwner(UserPermission role) =>
        role is UserPermission.ServerOwner;

    /// <summary>
    /// 是否为平台管理员
    /// </summary>
    public static bool IsPlatformAdmin(UserPermission role) =>
        role is UserPermission.PlatformAdmin;

    /// <summary>
    /// 是否为普通玩家
    /// </summary>
    public static bool IsNormalPlayer(UserPermission role) =>
        role is UserPermission.NormalPlayer;

    /// <summary>
    /// 角色是否可以管理目标角色
    /// </summary>
    public static bool CanManage(UserPermission actorRole, UserPermission targetRole)
    {
        // 平台管理员可以管理所有人
        if (actorRole == UserPermission.PlatformAdmin) return true;
        
        // 服主可以管理协管和普通玩家
        if (actorRole == UserPermission.ServerOwner)
            return targetRole is UserPermission.ServerModerator or UserPermission.NormalPlayer;
        
        // 协管只能管理普通玩家
        if (actorRole == UserPermission.ServerModerator)
            return targetRole == UserPermission.NormalPlayer;
        
        // 普通玩家不能管理任何人
        return false;
    }

    /// <summary>
    /// 是否可以任命协管
    /// </summary>
    public static bool CanAppointModerator(UserPermission actorRole) =>
        actorRole is UserPermission.ServerOwner or UserPermission.PlatformAdmin;

    /// <summary>
    /// 是否可以撤销协管权限
    /// </summary>
    public static bool CanRevokeModerator(UserPermission actorRole) =>
        actorRole is UserPermission.ServerOwner or UserPermission.PlatformAdmin;
}
