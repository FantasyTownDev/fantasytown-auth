// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Middleware;

/// <summary>
/// 拒绝封禁用户中间件
/// </summary>
public sealed class RejectBannedUserMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly CookieOptions AuthCookieDeleteOptions = new()
    {
        Path = "/",
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Lax
    };

    public RejectBannedUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPermissionSnapshot permissionSnapshot)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var uidClaim = context.User.FindFirst("uid");
            if (uidClaim != null && int.TryParse(uidClaim.Value, out var uid))
            {
                var snapshot = await permissionSnapshot.GetAsync(uid);

                if (snapshot == null)
                {
                    context.Response.Cookies.Delete("__Host-ft_auth", AuthCookieDeleteOptions);
                    context.Response.StatusCode = StatusCodes.Status302Found;
                    context.Response.Headers.Location = "/Account/Login";
                    return;
                }

                var isBanned = BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow);

                if (isBanned)
                {
                    context.Response.Cookies.Delete("__Host-ft_auth", AuthCookieDeleteOptions);
                    context.Response.StatusCode = StatusCodes.Status302Found;
                    context.Response.Headers.Location = "/Account/Login?banned=true";
                    return;
                }
            }
        }

        await _next(context);
    }
}
