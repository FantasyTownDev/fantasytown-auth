using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// /admin 页面基类（V5 §7.6）：操作者上下文（uid claim + PERM 快照实时角色）。
/// 导航与操作按钮的可见性按快照计算——撤权/封禁/软删在快照失效后即刻反映；
/// 隐藏 ≠ 授权：服务端策略与管线闸门双闸兜底（§7.6 视图投影规则）。
/// </summary>
public abstract class AdminPageModel : PageModel
{
    /// <summary>导航标记 ViewData 键：moderator 及以上</summary>
    public const string NavModeratorKey = "NavModerator";

    /// <summary>导航标记 ViewData 键：owner 及以上</summary>
    public const string NavOwnerKey = "NavOwner";

    private readonly IPermissionSnapshot _permissionSnapshot;
    private PermissionSnapshot? _actorSnapshot;
    private bool _actorSnapshotLoaded;

    protected AdminPageModel(IPermissionSnapshot permissionSnapshot)
        => _permissionSnapshot = permissionSnapshot;

    /// <summary>操作者 UID（/Admin 文件夹策略已保证已认证；防御性允许 null）</summary>
    protected int? ActorUid => User.GetUid();

    /// <summary>操作者 PERM 快照（请求内缓存；null = 未认证或防御性残留）</summary>
    protected async Task<PermissionSnapshot?> GetActorSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!_actorSnapshotLoaded)
        {
            _actorSnapshot = ActorUid is { } uid
                ? await _permissionSnapshot.GetAsync(uid, cancellationToken)
                : null;
            _actorSnapshotLoaded = true;
        }
        return _actorSnapshot;
    }

    /// <summary>
    /// 计算导航/投影所需的两档可见性标记并写入 ViewData。
    /// 快照封禁哨兵（IsBanned）按无特权处理——被封禁管理员不渲染任何管理入口。
    /// </summary>
    protected async Task SetNavFlagsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetActorSnapshotAsync(cancellationToken);
        var permission = snapshot?.Permission ?? UserPermission.NormalPlayer;
        var banned = snapshot?.IsBanned ?? true;

        ViewData[NavModeratorKey] = !banned && IsModeratorTier(permission);
        ViewData[NavOwnerKey] = !banned && IsOwnerTier(permission);
    }

    /// <summary>moderator 白名单（§5.2 现行口径：moderator/owner/platformadmin，精确匹配）</summary>
    protected static bool IsModeratorTier(UserPermission permission) =>
        permission is UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin;

    /// <summary>owner 白名单（§5.2 现行口径：owner/platformadmin，精确匹配）</summary>
    protected static bool IsOwnerTier(UserPermission permission) =>
        permission is UserPermission.ServerOwner or UserPermission.PlatformAdmin;
}
