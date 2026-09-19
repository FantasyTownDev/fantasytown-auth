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
