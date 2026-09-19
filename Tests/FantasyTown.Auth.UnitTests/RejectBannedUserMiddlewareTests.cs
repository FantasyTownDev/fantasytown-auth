using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FantasyTown.Auth.UnitTests;

public class RejectBannedUserMiddlewareTests
{
    private readonly IPermissionSnapshot _snapshot = new InMemoryPermissionSnapshot();

    private HttpContext CreateContext(int uid, bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            var claims = new List<Claim> { new Claim("uid", uid.ToString()) };
            var identity = new ClaimsIdentity(claims, "test");
            context.User = new ClaimsPrincipal(identity);
        }
        return context;
    }

    [Fact]
    public async Task InvokeAsync_NullSnapshot_RejectsSession()
    {
        var context = CreateContext(uid: 100);
        var middleware = new RejectBannedUserMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _snapshot);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Contains("/Account/Login", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_BannedUser_RejectsSession()
    {
        var uid = 200;
        var context = CreateContext(uid);
        await _snapshot.SetAsync(uid, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });

        var middleware = new RejectBannedUserMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _snapshot);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Contains("banned=true", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_RevokedPermission_RejectsSession()
    {
        var uid = 300;
        var context = CreateContext(uid);

        // 先设置权限快照（模拟已登录用户有协管权限）
        await _snapshot.SetAsync(uid, new PermissionSnapshot
        {
            Permission = UserPermission.ServerModerator,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        });

        // 撤销权限（清除快照）
        await _snapshot.RemoveAsync(uid);

        var middleware = new RejectBannedUserMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _snapshot);

        // 快照已清除 → null → 哨兵语义 → 拒绝
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Contains("/Account/Login", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain("banned=true", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_NormalUser_PassesThrough()
    {
        var uid = 400;
        var context = CreateContext(uid);
        await _snapshot.SetAsync(uid, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        });

        var nextCalled = false;
        var middleware = new RejectBannedUserMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, _snapshot);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedUser_PassesThrough()
    {
        var context = CreateContext(uid: 500, authenticated: false);

        var nextCalled = false;
        var middleware = new RejectBannedUserMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, _snapshot);

        Assert.True(nextCalled);
    }
}
