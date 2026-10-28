using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Admin.Players;

/// <summary>
/// 玩家封禁端点（V5 §7.3 POST /admin/players/{pid}/ban）：
/// 管线闸门（moderator 档位）→ 阶段五领域命令 → PRG。
/// </summary>
[ManagementGate(ManagementMatrix.PlayerModerator, Operation = "player-ban", RouteKey = "pid")]
public class BanModel : AdminPageModel
{
    private readonly BanPlayerHandler _handler;

    public BanModel(BanPlayerHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? Reason { get; set; }

    /// <summary>封禁截止（datetime-local 表单值；空 = 永久）</summary>
    [BindProperty]
    public string? Until { get; set; }

    /// <summary>PRG 回跳目标（详情页）</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int pid, CancellationToken cancellationToken)
    {
        // M3：解析失败此前静默落 until=null → 管理员意图临时却执行永久封禁
        DateTime? until = null;
        if (!string.IsNullOrWhiteSpace(Until))
        {
            if (!DateTime.TryParse(Until, out var parsed))
            {
                TempData["Error"] = "截止时间格式无效，封禁未执行，请检查后重新提交";
                return RedirectToPage("Detail", new { pid });
            }
            until = parsed.ToUniversalTime();
        }

        var result = await _handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = ActorUid ?? 0,
            Pid = pid,
            Reason = Reason ?? string.Empty,
            Until = until
        }, cancellationToken);

        if (result.IsSuccess)
        {
            TempData["Message"] = "封禁已生效";
        }
        else if (result.CurrentBannedUntil != null || result.ErrorMessage == "该玩家已处于封禁生效期")
        {
            // 409：返回当前生效封禁信息（§5.3）
            var untilText = result.CurrentBannedUntil?.ToString("yyyy-MM-dd HH:mm:ss") ?? "永久";
            TempData["Error"] = $"该玩家已处于封禁生效期（截止：{untilText}，理由：{result.CurrentBannedReason}）";
        }
        else
        {
            TempData["Error"] = result.ErrorMessage;
        }

        return RedirectToPage("Detail", new { pid });
    }
}
