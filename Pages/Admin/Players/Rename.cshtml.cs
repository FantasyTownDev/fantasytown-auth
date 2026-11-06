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
