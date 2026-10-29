// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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
    public async Task HandleAsync_BanUser_PublishesBanSignal()
    {
        // BAN_SIGNAL 广播接线：结构化字段供未来 Java Agent / 插件解析
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var publisher = new RecordingBanSignalPublisher();
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation(), publisher);
        var until = DateTime.UtcNow.AddHours(2);

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = "spam",
            Until = until
        });

        Assert.True(result.IsSuccess);
        var signal = Assert.Single(publisher.Published);
        Assert.Equal("ban", signal.Action);
        Assert.Equal("user", signal.Scope);
        Assert.Equal(1001, signal.Uid);
        Assert.Equal("user@test.com", signal.Email);
        Assert.Equal("spam", signal.Reason);
        Assert.Equal(until, signal.Until);
        Assert.Equal(1, signal.OperatorUid);
    }

    [Fact]
    public async Task HandleAsync_BanUserDenied_PublishesNothing()
    {
        // 授权拒绝路径不产生状态变化 → 不广播
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var publisher = new RecordingBanSignalPublisher();
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation(), publisher);

        var result = await handler.HandleAsync(new BanUserCommand { TargetUid = 1001, ActorUid = 1001 });

        Assert.False(result.IsSuccess);
        Assert.Empty(publisher.Published);
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

    [Fact]
    public async Task HandleAsync_OverlongReason_Failure()
    {
        // M2 链 b 回归：理由 >255 须命令侧友好拒绝，否则 DB 1406 → 500
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = new string('x', 256)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("封禁理由过长（最多 255 字符）", result.ErrorMessage);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.False(target.IsBanned); // 未落库
    }

    [Fact]
    public async Task HandleAsync_PastUntil_Failure()
    {
        // M4 回归：过期截止时间若直写 → IsBanned=true 且 IsBanEffective=false
        // （管理页显示已封禁、实际可登录）——须与 BanPlayerHandler 同样拒绝
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation());

        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = "expired",
            Until = DateTime.UtcNow.AddMinutes(-1)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("封禁截止时间必须晚于当前时间", result.ErrorMessage);
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        Assert.False(target.IsBanned); // 未落库
    }
}
