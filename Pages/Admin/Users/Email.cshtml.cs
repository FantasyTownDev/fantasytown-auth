using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 邮箱改绑端点（V5 §7.3 POST /admin/users/{uid}/email，非 ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "email-change", RouteKey = "uid")]
public class EmailModel : AdminPageModel
{
    private readonly ChangeUserEmailHandler _handler;

    public EmailModel(ChangeUserEmailHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? NewEmail { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new ChangeUserEmailCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid,
            NewEmail = NewEmail ?? string.Empty
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? $"邮箱已从 {result.OldEmail} 改绑"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
