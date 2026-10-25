using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Players;

/// <summary>
/// 玩家重置皮肤端点（V5 §7.3 POST /admin/players/{pid}/reset-texture）。
/// </summary>
[ManagementGate(ManagementMatrix.PlayerModerator, Operation = "texture-reset", RouteKey = "pid")]
public class ResetTextureModel : AdminPageModel
{
    private readonly ResetPlayerTextureHandler _handler;

    public ResetTextureModel(ResetPlayerTextureHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int pid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = ActorUid ?? 0,
            Pid = pid
        }, cancellationToken);

        TempData["Message"] = result.IsSuccess
            ? result.HadSkin ? "皮肤已重置" : "该玩家本就未佩戴皮肤"
            : null;
        if (!result.IsSuccess)
            TempData["Error"] = result.ErrorMessage;

        return RedirectToPage("Detail", new { pid });
    }
}
