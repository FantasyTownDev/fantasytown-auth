using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class SoftDeleteUserHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private async Task<(int uid, int pid, string name)> SeedUserAsync(AuthDbContext db)
    {
        // 授权前置（审计 H1）：操作者为服主（Uid=1），目标为普通玩家（Uid=1001）
        var owner = new User
        {
            Uid = 1,
            Email = "admin@test.com",
            Password = "hashed",
            Permission = UserPermission.ServerOwner,
            RegisterAt = DateTime.UtcNow
        };
        db.Users.Add(owner);

        var user = new User
        {
            Uid = 1001,
            Email = "test@test.com",
            Password = "hashed",
            Permission = UserPermission.NormalPlayer,
            RegisterAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var player = new Player
        {
            Uid = user.Uid,
            Name = "TestPlayer",
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        return (user.Uid, player.Pid, player.Name);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_SetsIsDeletedTrue()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == uid);
        Assert.True(user.IsDeleted);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_ReleasesPlayerName()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, pid, originalName) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ReleasedName);
        Assert.StartsWith($"deleted_{pid}_", result.ReleasedName);
        Assert.NotEqual(originalName, result.ReleasedName);

        var player = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Uid == uid);
        Assert.Equal(result.ReleasedName, player.Name);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_WritesAuthLog()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "user-soft-delete");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(uid, log.SubjectId);
        Assert.Contains("released_name=", log.Context);
    }

    [Fact]
    public async Task HandleAsync_DoubleSoftDelete_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        await handler.HandleAsync(new SoftDeleteUserCommand { TargetUid = uid, ActorUid = 1 });
        var second = await handler.HandleAsync(new SoftDeleteUserCommand { TargetUid = uid, ActorUid = 1 });

        Assert.False(second.IsSuccess);
        Assert.Contains("已软删除", second.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_NonExistentUser_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 9999,
            ActorUid = 1
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ReleasedName_AllowsNewRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, originalName) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var deleteResult = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(deleteResult.IsSuccess);

        // 同名应可注册（原名已释放为占位符）
        var newUser = new User
        {
            Uid = 2001,
            Email = "new@test.com",
            Password = "hashed",
            Permission = UserPermission.NormalPlayer,
            RegisterAt = DateTime.UtcNow
        };
        db.Users.Add(newUser);
        var newPlayer = new Player
        {
            Uid = newUser.Uid,
            Name = originalName, // 重用原名
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(newPlayer);
        await db.SaveChangesAsync();

        var player = await db.Players.FirstAsync(p => p.Name == originalName);
        Assert.Equal(newUser.Uid, player.Uid);
    }

    private static async Task SeedExtraUserAsync(AuthDbContext db, int uid, string email, UserPermission permission, bool isDeleted = false)
    {
        db.Users.Add(new User
        {
            Uid = uid,
            Email = email,
            Password = "hashed",
            Permission = permission,
            IsDeleted = isDeleted,
            RegisterAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 审计 H1：操作者不存在 → 拒绝 + admin-denied 审计
    /// </summary>
    [Fact]
    public async Task HandleAsync_ActorNonExistent_DeniedWithAudit()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 9999
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == uid)).IsDeleted);

        var denied = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied");
        Assert.NotNull(denied);
        Assert.Equal(9999, denied.ActorId);
        Assert.Equal(uid, denied.SubjectId);
        Assert.Equal("soft-delete", denied.Context);
    }

    /// <summary>
    /// 审计 H1：协管无软删权限（V5 §7.3 软删为 owner 级操作）
    /// </summary>
    [Fact]
    public async Task HandleAsync_ActorModerator_Denied()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        await SeedExtraUserAsync(db, 2, "mod@test.com", UserPermission.ServerModerator);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 2
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == uid)).IsDeleted);
    }

    /// <summary>
    /// 审计 H1 + 冻结 §7.2：服主不可软删另一服主（CanManage 同级拒绝）
    /// </summary>
    [Fact]
    public async Task HandleAsync_ActorOwner_TargetOwner_Denied()
    {
        using var db = CreateInMemoryDbContext();
        var (_, _, _) = await SeedUserAsync(db);
        await SeedExtraUserAsync(db, 1002, "owner2@test.com", UserPermission.ServerOwner);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 1002,
            ActorUid = 1
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == 1002)).IsDeleted);
    }

    /// <summary>
    /// 审计 H1 + 冻结 §7.2 规则二：服主不可软删自己（NotSelf，防自我毁损）
    /// </summary>
    [Fact]
    public async Task HandleAsync_ActorSelf_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 1,
            ActorUid = 1
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == 1)).IsDeleted);
    }

    /// <summary>
    /// 审计 H1：服主软删协管放行（CanManage(2,1) + NotSelf）
    /// </summary>
    [Fact]
    public async Task HandleAsync_ActorOwner_TargetModerator_Succeeds()
    {
        using var db = CreateInMemoryDbContext();
        await SeedUserAsync(db);
        await SeedExtraUserAsync(db, 1003, "mod2@test.com", UserPermission.ServerModerator);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 1003,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        Assert.True((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == 1003)).IsDeleted);
    }

    /// <summary>
    /// 审计 H1：已软删的操作者（全局过滤器排除）即刻丧失管理能力 → 拒绝
    /// </summary>
    [Fact]
    public async Task HandleAsync_SoftDeletedActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        await SeedExtraUserAsync(db, 3, "ghost@test.com", UserPermission.ServerOwner, isDeleted: true);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 3
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }
}
