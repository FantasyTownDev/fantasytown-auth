// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IClientIp _clientIp;
    private readonly IRegistrationQuotaService _registrationQuota;
    private readonly SiteOptions _siteOptions;
    private readonly IOwnerClaimLock _ownerClaimLock;

    public RegisterModel(
        AuthDbContext db,
        IPasswordService passwordService,
        IClientIp clientIp,
        IRegistrationQuotaService registrationQuota,
        SiteOptions siteOptions,
        IOwnerClaimLock ownerClaimLock)
    {
        _db = db;
        _passwordService = passwordService;
        _clientIp = clientIp;
        _registrationQuota = registrationQuota;
        _siteOptions = siteOptions;
        _ownerClaimLock = ownerClaimLock;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.Password != Input.ConfirmPassword)
        {
            ErrorMessage = "两次输入的密码不一致";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var clientIp = _clientIp.GetClientIp() ?? "unknown";

        var handler = new RegisterUserHandler(_db, _passwordService, _registrationQuota, _siteOptions, _ownerClaimLock);
        var command = new RegisterUserCommand
        {
            Username = Input.Username,
            Password = Input.Password,
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email,
            ClientIp = clientIp
        };

        var result = await handler.HandleAsync(command);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // 注册成功后自动登录
        await CookieAuthService.SignInAsync(
            HttpContext,
            result.UserId!.Value,
            result.Username!,
            Modules.Accounts.Domain.UserPermission.NormalPlayer);

        return RedirectToPage("/Index");
    }
}
