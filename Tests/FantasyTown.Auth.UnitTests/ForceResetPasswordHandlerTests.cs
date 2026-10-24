using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class ForceResetPasswordHandlerTests
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
            new User { Uid = 1, Email = "owner@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, SecurityStamp = "owner-stamp", RegisterAt = DateTime.UtcNow },
            new User { Uid = 1001, Email = "user@test.com", Password = "hashed", Permission = UserPermission.NormalPlayer, SecurityStamp = "user-stamp", RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_OwnerForceReset_RotatesStampAndAudits()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ForceResetPasswordHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ForceResetPasswordCommand
        {
            TargetUid = 1001,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.NotEqual("user-stamp", target.SecurityStamp);
        Assert.True(Guid.TryParse(target.SecurityStamp, out _));

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "password-force-reset");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
        Assert.Equal("security_stamp=rotated", log.Context);
    }

    [Fact]
    public async Task HandleAsync_ForceReset_RevokesUserTokens()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var revocation = new RecordingTokenRevocation();
        var handler = new ForceResetPasswordHandler(db, revocation);

        var result = await handler.HandleAsync(new ForceResetPasswordCommand
        {
            TargetUid = 1001,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        // H5：强制重置后以目标邮箱吊销全部 Ygg 令牌
        Assert.Equal(new[] { "user@test.com" }, revocation.RevokedEmails);
    }

    [Fact]
    public async Task HandleAsync_ModeratorActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new ForceResetPasswordHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ForceResetPasswordCommand { TargetUid = 1001, ActorUid = 1002 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.Equal("user-stamp", (await db.Users.FirstAsync(u => u.Uid == 1001)).SecurityStamp);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "password-force-reset"));
    }

    [Fact]
    public async Task HandleAsync_SelfForceReset_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ForceResetPasswordHandler(db, new RecordingTokenRevocation());

        // NotSelf（V5 §7.2 规则二）
        var result = await handler.HandleAsync(new ForceResetPasswordCommand { TargetUid = 1, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.Equal("owner-stamp", (await db.Users.FirstAsync(u => u.Uid == 1)).SecurityStamp);
    }

    [Fact]
    public async Task HandleAsync_TargetMissing_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ForceResetPasswordHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ForceResetPasswordCommand { TargetUid = 9999, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedTarget_Failure()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.IsDeleted = true;
        await db.SaveChangesAsync();
        var handler = new ForceResetPasswordHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ForceResetPasswordCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户已软删除", result.ErrorMessage);
    }
}
