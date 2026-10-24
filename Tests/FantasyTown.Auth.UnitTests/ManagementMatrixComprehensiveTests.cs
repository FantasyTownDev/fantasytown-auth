using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// V5 §7.2 越权矩阵全组合表驱动测试（P4 验收门；实施指导 §3.4「协管×玩家4级、服主×用户3级」全覆盖）。
/// 期望值由三闸门冻结语义机械推导（与闸门源码逐字对应），杜绝散点测试的组合盲区：
///   OwnerUser       = owner白名单 ∧ CanManage(actor,target)  ∧ NotSelf(actorUid,targetUid)
///   PlayerModerator = moderator白名单 ∧ CanManage(actor,属主) ∧ NotSelf(actorUid,属主Uid)
///   PlayerOwner     = owner白名单 ∧ CanManage(actor,属主)     ∧ NotSelf(actorUid,属主Uid)
///   ClearLockout    = moderator白名单 ∧ CanClearLockout（自我豁免，V5 评审 N1）
/// 每档位枚举 向下/向上/同级/自我 四象限；玩家域自我象限 = actor 名下拥有该玩家（uid 复用）。
/// </summary>
public class ManagementMatrixComprehensiveTests
{
    private const string PwHash = "hashed_x";

    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private static void SeedUser(AuthDbContext db, int uid, UserPermission permission)
    {
        db.Users.Add(new User
        {
            Uid = uid,
            Email = $"u{uid}@example.com",
            Password = PwHash,
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        db.SaveChanges();
    }

    private static int SeedPlayer(AuthDbContext db, int ownerUid, string name)
    {
        var player = new Player { Uid = ownerUid, Name = name, Uuid = Guid.NewGuid().ToString("N") };
        db.Players.Add(player);
        db.SaveChanges();
        return player.Pid;
    }

    private sealed class StubClientIp : IClientIp
    {
        public string? GetClientIp() => "203.0.113.9";
    }

    private sealed class NoopLockoutService : ILockoutService
    {
        public Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken ct = default)
            => Task.FromResult(new LockoutFailureResult(1, false));
        public Task<bool> IsLockedOutAsync(int uid, CancellationToken ct = default) => Task.FromResult(false);
        public Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken ct = default)
            => Task.FromResult<double?>(null);
        public Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetAsync(int uid, string email, string ip, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ClearAsync(int uid, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static ManagementPipeline CreatePipeline(AuthDbContext db) =>
        new(db, new InMemoryPasswordService(), new NoopLockoutService(), new StubClientIp());

    // —— 期望推导（与闸门源码逐字对应，非逐例手写）——
    private static bool OwnerUserExpected(UserPermission a, UserPermission t, bool self) =>
        a is UserPermission.ServerOwner or UserPermission.PlatformAdmin
        && ManagementGuard.CanManage(a, t) && !self;

    private static bool PlayerModExpected(UserPermission a, UserPermission owner, bool self) =>
        a is UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin
        && ManagementGuard.CanManage(a, owner) && !self;

    private static bool PlayerOwnerExpected(UserPermission a, UserPermission owner, bool self) =>
        a is UserPermission.ServerOwner or UserPermission.PlatformAdmin
        && ManagementGuard.CanManage(a, owner) && !self;

    private static bool ClearLockoutExpected(UserPermission a, UserPermission t, bool self) =>
        a is UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin
        && ManagementGuard.CanClearLockout(a, t, 1, self ? 1 : 2);

    // (actor, target/属主, 是否自我)。自我象限仅保留自洽组合（actor≡target）。
    public static TheoryData<UserPermission, UserPermission, bool> OwnerUserCombos
    {
        get
        {
            var d = new TheoryData<UserPermission, UserPermission, bool>();
            foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                foreach (var t in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                    d.Add(a, t, false); // 他象限 16
            foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                d.Add(a, a, true); // 自我象限 4（自洽）
            return d;
        }
    }

    public static TheoryData<UserPermission, UserPermission, bool> PlayerModeratorCombos => BuildPlayerCombos();
    public static TheoryData<UserPermission, UserPermission, bool> PlayerOwnerCombos => BuildPlayerCombos();

    private static TheoryData<UserPermission, UserPermission, bool> BuildPlayerCombos()
    {
        var d = new TheoryData<UserPermission, UserPermission, bool>();
        foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
            foreach (var o in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                d.Add(a, o, false); // 他象限 16
        foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
            d.Add(a, a, true); // 自我象限 4（actor 名下拥有该玩家）
        return d;
    }

    [Theory]
    [MemberData(nameof(OwnerUserCombos))]
    public async Task OwnerUser_FullMatrix(UserPermission actorPerm, UserPermission targetPerm, bool self)
    {
        using var db = CreateInMemoryDbContext();
        const int actorUid = 1;
        var targetUid = self ? actorUid : 2;
        SeedUser(db, actorUid, actorPerm);
        if (!self)
            SeedUser(db, targetUid, targetPerm);

        var result = await CreatePipeline(db).EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = actorUid,
            TargetUid = targetUid,
            Operation = "permission-change",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = false
        });

        Assert.Equal(OwnerUserExpected(actorPerm, targetPerm, self), result.IsAuthorized);
    }

    [Theory]
    [MemberData(nameof(PlayerModeratorCombos))]
    public async Task PlayerModerator_FullMatrix(UserPermission actorPerm, UserPermission ownerPerm, bool self)
    {
        using var db = CreateInMemoryDbContext();
        const int actorUid = 1;
        var ownerUid = self ? actorUid : 2;
        SeedUser(db, actorUid, actorPerm);
        if (!self)
            SeedUser(db, ownerUid, ownerPerm);
        var pid = SeedPlayer(db, ownerUid, $"p{actorPerm}{ownerPerm}{self}");

        var result = await CreatePipeline(db).EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = actorUid,
            TargetUid = 0,
            TargetPid = pid,
            Operation = "player-ban",
            Matrix = ManagementMatrix.PlayerModerator,
            Sensitive = false
        });

        Assert.Equal(PlayerModExpected(actorPerm, ownerPerm, self), result.IsAuthorized);
    }

    [Theory]
    [MemberData(nameof(PlayerOwnerCombos))]
    public async Task PlayerOwner_FullMatrix(UserPermission actorPerm, UserPermission ownerPerm, bool self)
    {
        using var db = CreateInMemoryDbContext();
        const int actorUid = 1;
        var ownerUid = self ? actorUid : 2;
        SeedUser(db, actorUid, actorPerm);
        if (!self)
            SeedUser(db, ownerUid, ownerPerm);
        var pid = SeedPlayer(db, ownerUid, $"r{actorPerm}{ownerPerm}{self}");

        var result = await CreatePipeline(db).EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = actorUid,
            TargetUid = 0,
            TargetPid = pid,
            Operation = "player-rename",
            Matrix = ManagementMatrix.PlayerOwner,
            Sensitive = false
        });

        Assert.Equal(PlayerOwnerExpected(actorPerm, ownerPerm, self), result.IsAuthorized);
    }

    // ClearLockout：moderator 白名单 × CanClearLockout。自我豁免（N1）是回归锚点。
    public static TheoryData<UserPermission, UserPermission, bool> ClearLockoutCombos
    {
        get
        {
            var d = new TheoryData<UserPermission, UserPermission, bool>();
            foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                foreach (var t in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                    d.Add(a, t, false); // 他象限 16
            foreach (var a in new[] { UserPermission.NormalPlayer, UserPermission.ServerModerator, UserPermission.ServerOwner, UserPermission.PlatformAdmin })
                d.Add(a, a, true); // 自我象限 4
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(ClearLockoutCombos))]
    public async Task ClearLockout_FullMatrix(UserPermission actorPerm, UserPermission targetPerm, bool self)
    {
        using var db = CreateInMemoryDbContext();
        const int actorUid = 1;
        var targetUid = self ? actorUid : 2;
        SeedUser(db, actorUid, actorPerm);
        if (!self)
            SeedUser(db, targetUid, targetPerm);

        var result = await CreatePipeline(db).EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = actorUid,
            TargetUid = targetUid,
            Operation = "lockout-clear",
            Matrix = ManagementMatrix.ClearLockout,
            Sensitive = false
        });

        Assert.Equal(ClearLockoutExpected(actorPerm, targetPerm, self), result.IsAuthorized);
    }
}
