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

    public RejectBannedUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPermissionSnapshot permissionSnapshot)
    {
        // 检查是否有用户 ID（已登录）
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            // 尝试获取用户 ID
            var uidClaim = context.User.FindFirst("uid");
            if (uidClaim != null && int.TryParse(uidClaim.Value, out var uid))
            {
                // 获取权限快照
                var snapshot = await permissionSnapshot.GetAsync(uid);
                
                // 哨兵语义：null（回源空/缓存失效）= 封禁等效，立即拒绝
                if (snapshot == null)
                {
                    context.Response.Cookies.Delete("__Host-ft_auth");
                    context.Response.Redirect("/Account/Login");
                    return;
                }

                // 检查是否被封禁
                var nowUtc = DateTime.UtcNow;
                var isBanned = BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, nowUtc);
                
                if (isBanned)
                {
                    // 封禁用户，清除 Cookie 并重定向到登录页面
                    context.Response.Cookies.Delete("__Host-ft_auth");
                    context.Response.Redirect("/Account/Login?banned=true");
                    return;
                }
            }
        }

        await _next(context);
    }
}
