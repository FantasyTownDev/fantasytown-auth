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
/// 用户封禁端点（V5 §7.3 POST /admin/users/{uid}/ban，非 ★）。
/// </summary>
[ManagementGate(ManagementMatrix.OwnerUser, Operation = "ban", RouteKey = "uid")]
public class BanModel : AdminPageModel
{
    private readonly BanUserHandler _handler;

    public BanModel(BanUserHandler handler, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _handler = handler;
    }

    [BindProperty]
    public string? Reason { get; set; }

    [BindProperty]
    public string? Until { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int uid, CancellationToken cancellationToken)
    {
        // M3：解析失败此前静默落 until=null → 管理员意图临时却执行永久封禁
        DateTime? until = null;
        if (!string.IsNullOrWhiteSpace(Until))
        {
            if (!DateTime.TryParse(Until, out var parsed))
            {
                TempData["Error"] = "截止时间格式无效，封禁未执行，请检查后重新提交";
                return RedirectToPage("Detail", new { uid });
            }
            until = parsed.ToUniversalTime();
        }

        var result = await _handler.HandleAsync(new BanUserCommand
        {
            ActorUid = ActorUid ?? 0,
            TargetUid = uid,
            Reason = Reason ?? string.Empty,
            Until = until
        }, cancellationToken);

        TempData[result.IsSuccess ? "Message" : "Error"] = result.IsSuccess
            ? "用户封禁已生效"
            : result.ErrorMessage;

        return RedirectToPage("Detail", new { uid });
    }
}
