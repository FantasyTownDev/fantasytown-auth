// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class ChangeUserEmailHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>种子：owner（Uid=1）+ 已验证 normal（Uid=1001）</summary>
    private static async Task SeedOwnerAndNormalAsync(AuthDbContext db)
    {
        db.Users.AddRange(
            new User { Uid = 1, Email = "owner@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, RegisterAt = DateTime.UtcNow },
            new User { Uid = 1001, Email = "user@test.com", Password = "hashed", Permission = UserPermission.NormalPlayer, Verified = true, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_OwnerChangesEmail_SucceedsAndResetsVerified()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewEmail = "newmail@test.com"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("user@test.com", result.OldEmail);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.Equal("newmail@test.com", target.Email);
        Assert.False(target.Verified);

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "email-change");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
        Assert.Equal("old=user@test.com;new=newmail@test.com", log.Context);
    }

    [Fact]
    public async Task HandleAsync_ChangeEmail_RevokesOldEmailTokens()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var revocation = new RecordingTokenRevocation();
        var handler = new ChangeUserEmailHandler(db, revocation);

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewEmail = "newmail@test.com"
        });

        Assert.True(result.IsSuccess);
        // H5：令牌链以 ID:{email} 为键，改绑后以旧邮箱吊销旧链
        Assert.Equal(new[] { "user@test.com" }, revocation.RevokedEmails);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public async Task HandleAsync_InvalidFormat_Failure(string newEmail)
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewEmail = newEmail
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("邮箱格式无效", result.ErrorMessage);
        Assert.Equal("user@test.com", (await db.Users.FirstAsync(u => u.Uid == 1001)).Email);
    }

    [Fact]
    public async Task HandleAsync_SameEmail_FailureWithoutAudit()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewEmail = "user@test.com"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("邮箱未变化", result.ErrorMessage);
        Assert.Empty(await db.AuthLogs.Where(l => l.Action == "email-change").ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_ModeratorActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1002,
            NewEmail = "stolen@test.com"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.Equal("user@test.com", (await db.Users.FirstAsync(u => u.Uid == 1001)).Email);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "email-change"));
    }

    [Fact]
    public async Task HandleAsync_SelfChange_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1,
            ActorUid = 1,
            NewEmail = "owner-new@test.com"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.Equal("owner@test.com", (await db.Users.FirstAsync(u => u.Uid == 1)).Email);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedTarget_Failure()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.IsDeleted = true;
        await db.SaveChangesAsync();
        var handler = new ChangeUserEmailHandler(db, new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new ChangeUserEmailCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewEmail = "x@test.com"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户已软删除", result.ErrorMessage);
    }
}
