using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 用户解封端点（V5 §7.3 POST /admin/users/{uid}/unban）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "unban", RouteKey = "uid")]
public class UnbanModel : AdminPageModel
{
    private readonly UnbanUserHandler _handler;

    public UnbanModel(UnbanUserHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new UnbanUserCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.ErrorMessage;
        }
        else
        {
            TempData["Message"] = result.WasBanned ? "用户封禁已解除" : "该用户本就未处于封禁（幂等成功，无变化）";
        }

        return RedirectToPage("Detail", new { uid });
    }
}
