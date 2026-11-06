// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class ChangeUserPermissionHandlerTests
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
    public async Task HandleAsync_OwnerAppointsModerator_Succeeds()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var snapshot = new InMemoryPermissionSnapshot();
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.NormalPlayer });
        var handler = new ChangeUserPermissionHandler(db, snapshot);

        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewPermission = UserPermission.ServerModerator
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(UserPermission.NormalPlayer, result.OldPermission);
        Assert.Equal(UserPermission.ServerModerator, result.NewPermission);
        Assert.Equal(UserPermission.ServerModerator, (await db.Users.FirstAsync(u => u.Uid == 1001)).Permission);

        // DEL PERM：快照被清除，下一请求回源按新权限重建
        Assert.Null(await snapshot.GetAsync(1001));

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "permission-change");
        Assert.NotNull(log);
        Assert.Equal(1, log.ActorId);
        Assert.Equal(1001, log.SubjectId);
        Assert.Equal("old=0,new=1", log.Context);
    }

    [Fact]
    public async Task HandleAsync_OwnerRevokesModerator_Succeeds()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1002,
            ActorUid = 1,
            NewPermission = UserPermission.NormalPlayer
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(UserPermission.NormalPlayer, (await db.Users.FirstAsync(u => u.Uid == 1002)).Permission);
        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "permission-change");
        Assert.Equal("old=1,new=0", log!.Context);
    }

    /// <summary>§7.4：提权到 owner/PlatformAdmin 属非法目标角色 → admin-denied</summary>
    [Theory]
    [InlineData(UserPermission.ServerOwner)]
    [InlineData(UserPermission.PlatformAdmin)]
    public async Task HandleAsync_IllegalTargetRole_Denied(UserPermission illegalRole)
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewPermission = illegalRole
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("目标角色非法", result.ErrorMessage);
        Assert.Equal(UserPermission.NormalPlayer, (await db.Users.FirstAsync(u => u.Uid == 1001)).Permission);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "permission-change:illegal-role"));
    }

    [Fact]
    public async Task HandleAsync_ModeratorActor_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        db.Users.Add(new User { Uid = 1002, Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        // 协管对普通玩家 CanManage(1,0)=true，但软删/权限变更是 owner 级操作 → 拒绝
        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1001,
            ActorUid = 1002,
            NewPermission = UserPermission.ServerModerator
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.NotNull(await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "admin-denied" && l.Context == "permission-change"));
    }

    [Fact]
    public async Task HandleAsync_SelfChange_Denied()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        // NotSelf：服主不可自我降权（防站点失控）
        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1,
            ActorUid = 1,
            NewPermission = UserPermission.NormalPlayer
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.Equal(UserPermission.ServerOwner, (await db.Users.FirstAsync(u => u.Uid == 1)).Permission);
    }

    [Fact]
    public async Task HandleAsync_SameRole_FailureWithoutStateChange()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewPermission = UserPermission.NormalPlayer
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("目标角色未变化", result.ErrorMessage);
        Assert.Empty(await db.AuthLogs.Where(l => l.Action == "permission-change").ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_TargetMissing_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var handler = new ChangeUserPermissionHandler(db, new InMemoryPermissionSnapshot());

        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 9999,
            ActorUid = 1,
            NewPermission = UserPermission.ServerModerator
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("用户不存在", result.ErrorMessage);
    }
}
