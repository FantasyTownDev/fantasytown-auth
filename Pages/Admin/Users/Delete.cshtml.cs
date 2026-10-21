using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 软删账户端点（V5 §7.3 POST /admin/users/{uid}/delete ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "soft-delete", RouteKey = "uid", Sensitive = true)]
public class DeleteModel : AdminPageModel
{
    private readonly SoftDeleteUserHandler _handler;

    public DeleteModel(SoftDeleteUserHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new SoftDeleteUserCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? $"账户已软删（释放角色名占位：{result.ReleasedName}）"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
