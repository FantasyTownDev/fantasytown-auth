using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Players;

/// <summary>
/// 玩家解封端点（V5 §7.3 POST /admin/players/{pid}/unban）：
/// 0 行按幂等成功处理并提示（防错 #7）；附再犯提醒（历史封禁计数）。
/// </summary>
[ManagementGate(ManagementMatrix.PlayerModerator, Operation = "player-unban", RouteKey = "pid")]
public class UnbanModel : AdminPageModel
{
    private readonly UnbanPlayerHandler _handler;

    public UnbanModel(UnbanPlayerHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int pid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new UnbanPlayerCommand
        {
            ActorUid = ActorUid ?? 0,
            Pid = pid
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.ErrorMessage;
        }
        else if (!result.WasBanned)
        {
            TempData["Message"] = "该玩家本就未处于封禁（幂等成功，无变化）";
        }
        else
        {
            var repeat = result.PriorBanCount >= 2
                ? $"（该玩家历史封禁 {result.PriorBanCount} 次，请注意再犯）"
                : string.Empty;
            TempData["Message"] = $"封禁已解除{repeat}";
        }

        return RedirectToPage("Detail", new { pid });
    }
}
