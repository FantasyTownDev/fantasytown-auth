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
/// 软删账户端点（V5 §7.3 POST /admin/users/{uid}/delete ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "soft-delete", RouteKey = "uid", Sensitive = true)]
public class DeleteModel : AdminPageModel
{
    private readonly SoftDeleteUserHandler _handler;

    public DeleteModel(SoftDeleteUserHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new SoftDeleteUserCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? $"账户已软删（释放角色名占位：{result.ReleasedName}）"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
