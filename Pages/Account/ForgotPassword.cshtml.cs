// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ForgotPasswordModel(AuthDbContext db, IPasswordService passwordService, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _passwordService = passwordService;
        _dataProtectionProvider = dataProtectionProvider;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var handler = new PasswordResetHandler(_db, _passwordService, _dataProtectionProvider);
        var command = new RequestPasswordResetCommand
        {
            Email = Input.Email
        };

        var result = await handler.HandleRequestAsync(command);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // 安全考虑：无论邮箱是否存在都显示成功消息
        // TODO: MailService 实现后将 result.ResetToken 嵌入重置链接发送（V5 §4.5）
        SuccessMessage = "如果该邮箱已注册，您将收到一封密码重置邮件";
        return Page();
    }
}
