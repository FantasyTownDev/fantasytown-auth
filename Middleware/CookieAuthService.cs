using System.Security.Claims;
using FantasyTown.Auth.Modules.Accounts.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FantasyTown.Auth.Middleware;

/// <summary>
/// Cookie 认证服务
/// </summary>
public static class CookieAuthService
{
    /// <summary>
    /// 创建认证 Cookie
    /// </summary>
    public static async Task SignInAsync(HttpContext context, int uid, string username, UserPermission permission)
    {
        var claims = new List<Claim>
        {
            new Claim("uid", uid.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, permission.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24),
            AllowRefresh = true
        };

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }

    /// <summary>
    /// 登出
    /// </summary>
    public static async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
