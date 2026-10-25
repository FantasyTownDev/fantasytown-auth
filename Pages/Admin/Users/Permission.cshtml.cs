using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 权限变更端点（V5 §7.3 POST /admin/users/{uid}/permission ★，需当前密码二次确认）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "permission-change", RouteKey = "uid", Sensitive = true)]
public class PermissionModel : AdminPageModel
{
    private readonly ChangeUserPermissionHandler _handler;

    public PermissionModel(ChangeUserPermissionHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? NewPermission { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<UserPermission>(NewPermission, out var permission)
            || permission is not (UserPermission.NormalPlayer or UserPermission.ServerModerator))
        {
            TempData["Error"] = "非法的权限值";
            return RedirectToPage("Detail", new { uid });
        }

        var result = await _handler.HandleAsync(new ChangeUserPermissionCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid,
            NewPermission = permission
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? $"权限已变更为 {result.NewPermission}"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
