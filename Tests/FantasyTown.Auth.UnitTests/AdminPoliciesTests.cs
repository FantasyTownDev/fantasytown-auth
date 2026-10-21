using System.Security.Claims;
using FantasyTown.Auth.Modules.Accounts.Application;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// V5 §5.2 鉴权策略测试：
/// 防错 #1（只信 uid claim，不读 Cookie Role claim）、防错 #2（null 快照按封禁等效拒绝）、
/// 角色白名单精确匹配 + 封禁时间谓词。
/// </summary>
public class AdminPoliciesTests
{
    private static PermissionSnapshot Snap(UserPermission role, bool banned = false, DateTime? until = null) =>
        new() { Permission = role, IsBanned = banned, BannedUntil = until };

    private static readonly DateTime NowUtc = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    // ===== IsSatisfied 纯谓词 =====

    [Theory]
    [InlineData(UserPermission.ServerOwner, true)]         // 服主
    [InlineData(UserPermission.PlatformAdmin, true)]       // 平台管理位（预留，天然通过 owner 层）
    [InlineData(UserPermission.ServerModerator, false)]    // 协管不满足 owner 层
    [InlineData(UserPermission.NormalPlayer, false)]       // 普通玩家不满足 owner 层
    public void IsSatisfied_OwnerTier_UsesWhitelist(UserPermission role, bool expected)
    {
        Assert.Equal(expected, AdminPolicies.IsSatisfied(Snap(role), ownerTier: true, NowUtc));
    }

    [Theory]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    [InlineData(UserPermission.ServerModerator, true)]     // 协管满足 moderator 层
    [InlineData(UserPermission.NormalPlayer, false)]
    public void IsSatisfied_ModeratorTier_UsesWhitelist(UserPermission role, bool expected)
    {
        Assert.Equal(expected, AdminPolicies.IsSatisfied(Snap(role), ownerTier: false, NowUtc));
    }

    [Fact]
    public void IsSatisfied_NullSnapshot_DeniesAsBanEquivalent()
    {
        // 防错 #2：null 快照是防御性残留值，按封禁等效拒绝，绝不放行
        Assert.False(AdminPolicies.IsSatisfied(null, ownerTier: true, NowUtc));
        Assert.False(AdminPolicies.IsSatisfied(null, ownerTier: false, NowUtc));
    }

    [Theory]
    [InlineData(true, null, false)]                         // 永久封禁生效
    [InlineData(true, "future", false)]                     // 临时封禁未到期
    [InlineData(true, "past", true)]                        // 临时封禁已过期 → 时间谓词放行
    [InlineData(false, null, true)]                         // 未封禁
    [InlineData(false, "future", true)]
    public void IsSatisfied_BanTimePredicate_GatesAccess(bool banned, string? untilCase, bool expected)
    {
        DateTime? until = untilCase switch
        {
            "future" => NowUtc.AddDays(1),
            "past" => NowUtc.AddDays(-1),
            _ => null
        };

        Assert.Equal(expected, AdminPolicies.IsSatisfied(Snap(UserPermission.ServerOwner, banned, until), ownerTier: true, NowUtc));
    }

    // ===== EvaluateAsync：uid claim → PERM 快照 =====

    private sealed class StubPermissionSnapshot : IPermissionSnapshot
    {
        private readonly PermissionSnapshot? _snapshot;
        public StubPermissionSnapshot(PermissionSnapshot? snapshot) => _snapshot = snapshot;

        public Task<PermissionSnapshot?> GetAsync(int uid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(int uid, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(NowUtc);
    }

    private static ServiceProvider BuildServices(PermissionSnapshot? snapshot)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionSnapshot>(new StubPermissionSnapshot(snapshot));
        services.AddSingleton<IClock, FixedClock>();
        return services.BuildServiceProvider();
    }

    private static AuthorizationHandlerContext Context(ClaimsPrincipal user, object? resource) =>
        new(Array.Empty<IAuthorizationRequirement>(), user, resource);

    private static ClaimsPrincipal PrincipalWithUid(int uid, UserPermission roleClaim)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("uid", uid.ToString()), new Claim(ClaimTypes.Role, roleClaim.ToString()) },
            "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task EvaluateAsync_RoleClaimOwnerButSnapshotPlayer_Denies()
    {
        // 防错 #1：Cookie Role claim 在登录时固化、可与快照脱节——策略只信 uid claim + PERM 快照
        var principal = PrincipalWithUid(1, UserPermission.ServerOwner);
        var http = new DefaultHttpContext { RequestServices = BuildServices(Snap(UserPermission.NormalPlayer)) };

        Assert.False(await AdminPolicies.EvaluateAsync(Context(principal, http), ownerTier: true));
    }

    [Fact]
    public async Task EvaluateAsync_OwnerSnapshotViaHttpContext_Allows()
    {
        var principal = PrincipalWithUid(1, UserPermission.ServerOwner);
        var http = new DefaultHttpContext { RequestServices = BuildServices(Snap(UserPermission.ServerOwner)) };

        Assert.True(await AdminPolicies.EvaluateAsync(Context(principal, http), ownerTier: true));
    }

    [Fact]
    public async Task EvaluateAsync_BannedOwnerSnapshot_Denies()
    {
        var principal = PrincipalWithUid(1, UserPermission.ServerOwner);
        var http = new DefaultHttpContext { RequestServices = BuildServices(Snap(UserPermission.ServerOwner, banned: true)) };

        Assert.False(await AdminPolicies.EvaluateAsync(Context(principal, http), ownerTier: true));
    }

    [Fact]
    public async Task EvaluateAsync_MissingUidClaim_Denies()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "ServerOwner") }, "test");
        var http = new DefaultHttpContext { RequestServices = BuildServices(Snap(UserPermission.ServerOwner)) };

        Assert.False(await AdminPolicies.EvaluateAsync(Context(new ClaimsPrincipal(identity), http), ownerTier: true));
    }

    [Fact]
    public async Task EvaluateAsync_MissingResource_FailsClosed()
    {
        var principal = PrincipalWithUid(1, UserPermission.ServerOwner);
        Assert.False(await AdminPolicies.EvaluateAsync(Context(principal, resource: null), ownerTier: true));
    }
}
