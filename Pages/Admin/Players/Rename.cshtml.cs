using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Players;

/// <summary>
/// 玩家改名端点（V5 §7.3 POST /admin/players/{pid}/rename，owner 档位；uuid 不变）。
/// </summary>
[ManagementGate(ManagementMatrix.PlayerOwner, Operation = "player-rename", RouteKey = "pid")]
public class RenameModel : AdminPageModel
{
    private readonly RenamePlayerHandler _handler;

    public RenameModel(RenamePlayerHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? NewName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int pid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = ActorUid ?? 0,
            Pid = pid,
            NewName = NewName ?? string.Empty
        }, cancellationToken);

        if (result.IsSuccess)
        {
            TempData["Message"] = $"角色名已从 {result.OldName} 改为 {NewName}";
        }
        else
        {
            TempData["Error"] = result.ErrorMessage;
        }

        return RedirectToPage("Detail", new { pid });
    }
}
