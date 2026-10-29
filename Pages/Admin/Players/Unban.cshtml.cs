// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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
