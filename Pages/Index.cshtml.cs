using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages;

public class IndexModel : PageModel
{
    private readonly IPermissionSnapshot _permissionSnapshot;

    public IndexModel(IPermissionSnapshot permissionSnapshot)
        => _permissionSnapshot = permissionSnapshot;

    /// <summary>特权用户显示 /admin 跳转入口（V5 §7.0：普通页面不内嵌任何管理表单）</summary>
    public bool ShowAdminEntry { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // PERM 快照实时判定：撤权/封禁/软删即刻反映；封禁哨兵不渲染入口
        var snapshot = User.GetUid() is { } uid
            ? await _permissionSnapshot.GetAsync(uid, cancellationToken)
            : null;
        ShowAdminEntry = snapshot is { IsBanned: false }
            && snapshot.Permission is UserPermission.ServerModerator
                or UserPermission.ServerOwner
                or UserPermission.PlatformAdmin;
    }
}
