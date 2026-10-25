using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Lockouts;

/// <summary>
/// 解除账号锁定端点（V5 §7.3 POST /admin/lockouts/{uid}/clear，moderator 策略）。
/// Guard 走 ClearLockout 档位：目标即本人直接放行（N1 自救），他人须 CanManage。
/// </summary>
[ManagementGate(ManagementMatrix.ClearLockout, Operation = "lockout-clear", RouteKey = "uid")]
public class ClearModel : AdminPageModel
{
    private readonly ClearUserLockoutHandler _handler;

    public ClearModel(ClearUserLockoutHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new ClearUserLockoutCommand
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
            TempData["Message"] = result.WasLocked
                ? "账号锁定已解除"
                : "该账户本就未处于锁定（幂等成功，无变化）";
        }

        return RedirectToPage("Index");
    }
}
