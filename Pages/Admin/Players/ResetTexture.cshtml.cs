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
