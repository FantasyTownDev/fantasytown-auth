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
