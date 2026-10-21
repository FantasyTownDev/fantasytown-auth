using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 强制重置密码端点（V5 §7.3 POST /admin/users/{uid}/force-reset-password ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "password-force-reset", RouteKey = "uid", Sensitive = true)]
public class ForceResetPasswordModel : AdminPageModel
{
    private readonly ForceResetPasswordHandler _handler;

    public ForceResetPasswordModel(ForceResetPasswordHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new ForceResetPasswordCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? "已强制重置：目标用户下次登录须改密"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
