// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class ClearUserLockoutHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>种子：owner（Uid=1）+ normal（Uid=1001）；锁定服务用生产 InMemory 实现</summary>
    private static async Task<(AuthDbContext db, FantasyTown.Auth.Modules.Accounts.Infrastructure.InMemoryLockoutService lockout)> CreateSeededDbAsync()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new AuthDbContext(options);
        db.Users.AddRange(
            new User { Uid = 1, Email = "owner@test.com", Password = "hashed", Permission = UserPermission.ServerOwner, RegisterAt = DateTime.UtcNow },
            new User { Uid = 1001, Email = "user@test.com", Password = "hashed", Permission = UserPermission.NormalPlayer, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (db, new FantasyTown.Auth.Modules.Accounts.Infrastructure.InMemoryLockoutService());
    }

    private static async Task LockAsync(FantasyTown.Auth.Modules.Accounts.Infrastructure.InMemoryLockoutService lockout, int uid, string email)
    {
        for (int i = 0; i < 5; i++)
        {
            await lockout.RecordFailureAsync(uid, email, "127.0.0.1");
        }
    }

    [Fact]
    public async Task HandleAsync_OwnerClearsLockedUser_Succeeds()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        await LockAsync(lockout, 1001, "user@test.com");
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.True(result.IsSuccess);
        Assert.True(result.WasLocked);
        Assert.False(await lockout.IsLockedOutAsync(1001));

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "lockout-cleared");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
        Assert.Equal("was_locked=true", log.Context);
    }

    /// <summary>moderator 策略：协管可解普通玩家的锁（CanClearLockout → CanManage(1,0)）</summary>
    [Fact]
    public async Task HandleAsync_ModeratorClearsNormalUser_Succeeds()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await LockAsync(lockout, 1001, "user@test.com");
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1001, ActorUid = 1002 });

        Assert.True(result.IsSuccess);
        Assert.True(result.WasLocked);
        Assert.False(await lockout.IsLockedOutAsync(1001));
    }

    /// <summary>V5 §7.2：协管不能解服主的锁（CanClearLockout → CanManage(1,2)=false）</summary>
    [Fact]
    public async Task HandleAsync_ModeratorClearsOwner_Denied()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await LockAsync(lockout, 1, "owner@test.com");
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1, ActorUid = 1002 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.True(await lockout.IsLockedOutAsync(1));
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "lockout-clear"));
    }

    /// <summary>V5 评审 N1 唯一自我放行通道：被锁服主自救（CanClearLockout 自我放行）</summary>
    [Fact]
    public async Task HandleAsync_SelfClear_AlwaysAllowed()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        await LockAsync(lockout, 1, "owner@test.com");
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1, ActorUid = 1 });

        Assert.True(result.IsSuccess);
        Assert.True(result.WasLocked);
        Assert.False(await lockout.IsLockedOutAsync(1));
    }

    [Fact]
    public async Task HandleAsync_NormalPlayerActor_Denied()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1001, ActorUid = 1001 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ActorNonExistent_Denied()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1001, ActorUid = 9999 });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_NoLock_IdempotentSuccessStillAudits()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 1001, ActorUid = 1 });

        Assert.True(result.IsSuccess);
        Assert.False(result.WasLocked);
        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "lockout-cleared");
        Assert.Equal("was_locked=false", log!.Context);
    }

    [Fact]
    public async Task HandleAsync_TargetMissing_ReturnsNotFound()
    {
        var (db, lockout) = await CreateSeededDbAsync();
        var handler = new ClearUserLockoutHandler(db, lockout);

        var result = await handler.HandleAsync(new ClearUserLockoutCommand { TargetUid = 9999, ActorUid = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户不存在", result.ErrorMessage);
    }
}
