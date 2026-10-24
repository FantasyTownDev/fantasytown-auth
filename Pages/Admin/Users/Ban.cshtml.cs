using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 用户封禁端点（V5 §7.3 POST /admin/users/{uid}/ban，非 ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "ban", RouteKey = "uid")]
public class BanModel : AdminPageModel
{
    private readonly BanUserHandler _handler;

    public BanModel(BanUserHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? Reason { get; set; }

    [BindProperty]
    public string? Until { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        DateTime? until = null;
        if (!string.IsNullOrWhiteSpace(Until) && DateTime.TryParse(Until, out var parsed))
            until = parsed.ToUniversalTime();

        var result = await _handler.HandleAsync(new BanUserCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid,
            Reason = Reason ?? string.Empty,
            Until = until
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? "用户封禁已生效"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
