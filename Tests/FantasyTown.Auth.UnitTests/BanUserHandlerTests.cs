using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class BanUserHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>种子：owner（Uid=1）+ normal（Uid=1001）</summary>
    private static async Task SeedOwnerAndNormalAsync(AuthDbContext db)
    {
        db.Users.AddRange(
            new User { Uid = 1, Email = "owner@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, RegisterAt = DateTime.UtcNow },
            new User { Uid = 1001, Email = "user@test.com", Password = "hashed", Permission = UserPermission.NormalPlayer, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_OwnerBansUser_SucceedsWithPermInvalidation()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var snapshot = new InMemoryPermissionSnapshot();
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.NormalPlayer });
        var handler = new BanUserHandler(db, snapshot, new RecordingTokenRevocation());
        var until = DateTime.UtcNow.AddHours(2);

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = "spam",
            Until = until
        });

        Assert.True(result.IsSuccess);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.True(target.IsBanned);
        Assert.Equal("spam", target.BannedReason);
        Assert.Equal(until, target.BannedUntil);

        // DEL PERM：快照被清除
        Assert.Null(await snapshot.GetAsync(1001));

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "ban");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
        Assert.Contains("reason=spam", log.Context);
        Assert.Contains(until.ToString("O"), log.Context);
    }

    [Fact]
    public async Task HandleAsync_BanUser_RevokesTargetTokens()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var snapshot = new InMemoryPermissionSnapshot();
        var revocation = new RecordingTokenRevocation();
        var handler = new BanUserHandler(db, snapshot, revocation);

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = "token-revocation"
        });

        Assert.True(result.IsSuccess);
        // H5：封禁后以目标邮箱吊销全部 Ygg 令牌
        Assert.Equal(new[] { "user@test.com" }, revocation.RevokedEmails);
    }

    [Fact]
    public async Task HandleAsync_PermanentBan_AuditsPermanent()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.True(target.IsBanned);
        Assert.Null(target.BannedUntil);
        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "ban");
        Assert.Contains("until=permanent", log!.Context);
    }

    [Fact]
    public async Task HandleAsync_Reban_OverwritesAndAuditsAgain()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        await handler.HandleAsync(new BanUserCommand { TargetUid = 1001, ActorUid = 1, Reason = "first" });
        var second = await handler.HandleAsync(new BanUserCommand { TargetUid = 1001, ActorUid = 1, Reason = "second" });

        Assert.True(second.IsSuccess);
        Assert.Equal("second", (await db.Users.FirstAsync(u => u.Uid == 1001)).BannedReason);
        Assert.Equal(2, await db.AuthLogs.CountAsync(l => l.Action == "ban"));
    }

    [Fact]
    public async Task HandleAsync_ModeratorActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        // 协管封禁玩家走 player 域（§7.3 玩家封禁）；账户封禁是 owner 级操作
        var result = await handler.HandleAsync(new BanUserCommand { TargetUid = 1001, ActorUid = 1002 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.FirstAsync(u => u.Uid == 1001)).IsBanned);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "ban"));
    }

    [Fact]
    public async Task HandleAsync_SelfBan_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        // NotSelf：防自我封禁导致站点失控（V5 §7.2 规则二）
        var result = await handler.HandleAsync(new BanUserCommand { TargetUid = 1, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Users.FirstAsync(u => u.Uid == 1)).IsBanned);
    }

    [Fact]
    public async Task HandleAsync_TargetIsAnotherOwner_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "owner2@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new BanUserCommand { TargetUid = 1002, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedTarget_Failure()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.IsDeleted = true;
        await db.SaveChangesAsync();
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new BanUserCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户已软删除", result.ErrorMessage);
    }
}
