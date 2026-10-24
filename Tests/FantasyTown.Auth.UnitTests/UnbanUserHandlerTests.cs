using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class UnbanUserHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>种子：owner（Uid=1）+ 被封 normal（Uid=1001）</summary>
    private static async Task SeedOwnerAndBannedNormalAsync(AuthDbContext db)
    {
        db.Users.AddRange(
            new User { Uid = 1, Email = "owner@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, RegisterAt = DateTime.UtcNow },
            new User
            {
                Uid = 1001,
                Email = "user@test.com",
                Password = "hashed",
                Permission = UserPermission.NormalPlayer,
                IsBanned = true,
                BannedReason = "spam",
                BannedUntil = DateTime.UtcNow.AddHours(1),
                RegisterAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_OwnerUnbans_SucceedsWithPermInvalidation()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndBannedNormalAsync(db);
        var snapshot = new InMemoryPermissionSnapshot();
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.NormalPlayer, IsBanned = true });
        var handler = new UnbanUserHandler(db, snapshot);

        var result = await handler.HandleAsync(new UnbanUserCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.True(result.IsSuccess);
        Assert.True(result.WasBanned);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.False(target.IsBanned);
        Assert.Null(target.BannedReason);
        Assert.Null(target.BannedUntil);

        // DEL PERM：快照被清除
        Assert.Null(await snapshot.GetAsync(1001));

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "unban");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
    }

    [Fact]
    public async Task HandleAsync_NotBanned_IdempotentSuccessWithoutAudit()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndBannedNormalAsync(db);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.IsBanned = false;
        await db.SaveChangesAsync();
        var handler = new UnbanUserHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new UnbanUserCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.True(result.IsSuccess);
        Assert.False(result.WasBanned);
        Assert.Empty(await db.AuthLogs.Where(l => l.Action == "unban").ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_ModeratorActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndBannedNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new UnbanUserHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new UnbanUserCommand { TargetUid = 1001, ActorUid = 1002 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.True((await db.Users.FirstAsync(u => u.Uid == 1001)).IsBanned);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "unban"));
    }

    [Fact]
    public async Task HandleAsync_SelfUnban_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndBannedNormalAsync(db);
        var owner = await db.Users.FirstAsync(u => u.Uid == 1);
        owner.IsBanned = true;
        await db.SaveChangesAsync();
        var handler = new UnbanUserHandler(db, new InMemoryPermissionSnapshot());

        // NotSelf：解封同样禁止自我操作（唯一自我放行通道是 CanClearLockout，§7.2）
        var result = await handler.HandleAsync(new UnbanUserCommand { TargetUid = 1, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.True((await db.Users.FirstAsync(u => u.Uid == 1)).IsBanned);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedTarget_Failure()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndBannedNormalAsync(db);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.IsDeleted = true;
        await db.SaveChangesAsync();
        var handler = new UnbanUserHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new UnbanUserCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户已软删除", result.ErrorMessage);
    }
}
