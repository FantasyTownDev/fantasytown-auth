// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 玩家改名命令集成测试（DB UNIQUE 冲突捕获路径，防错 #16）。
/// </summary>
[Trait("Category", "Integration")]
[Collection("MariaDB")]
public class RenamePlayerIntegrationTests : IAsyncLifetime
{
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        if (DockerHelper.IsAvailable())
        {
            var container = new Testcontainers.MySql.MySqlBuilder("mysql:8.4")
                .WithDatabase("fantasytown_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await container.StartAsync();
            _connectionString = container.GetConnectionString();
        }
        else
        {
            _connectionString = TestDbContextFactory.GetConnectionString();
        }

        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString))
            .Options;
        return new AuthDbContext(options);
    }

    private static (int OwnerUid, int Pid) SeedPlayer(AuthDbContext db, string name)
    {
        var owner = new User
        {
            Email = $"rename-owner-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();

        var player = new Player
        {
            Uid = owner.Uid,
            Name = name,
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(player);
        db.SaveChanges();
        return (owner.Uid, player.Pid);
    }

    private static User SeedOwnerAdmin(AuthDbContext db)
    {
        var admin = new User
        {
            Email = $"rename-admin-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.ServerOwner,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(admin);
        db.SaveChanges();
        return admin;
    }

    [Fact]
    public async Task Rename_DuplicateName_ReturnsOccupiedFailure()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var takenName = $"taken{rid}";
        SeedPlayer(db, takenName);
        var (_, pid) = SeedPlayer(db, $"mine{rid}");
        var admin = SeedOwnerAdmin(db);

        var cache = new RecordingPlayerCacheInvalidation();
        var handler = new RenamePlayerHandler(db, cache);
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            NewName = takenName
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("角色名已被占用", result.ErrorMessage);
        // 名称与 uuid 均未变化
        Assert.Equal($"mine{rid}", (await db.Players.AsNoTracking().FirstAsync(p => p.Pid == pid)).Name);
        // 失败路径不触发缓存失效（H5 副作用仅随成功落库）
        Assert.Empty(cache.InvalidatedUuids);
    }

    [Fact]
    public async Task Rename_OwnersPlayerByModerator_IsDenied()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedPlayer(db, $"boss{rid}");
        db.Users.First(u => u.Uid == ownerUid).Permission = UserPermission.ServerOwner;
        await db.SaveChangesAsync();
        var moderator = new User
        {
            Email = $"rename-mod-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.ServerModerator,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(moderator);
        db.SaveChanges();

        var cache = new RecordingPlayerCacheInvalidation();
        var handler = new RenamePlayerHandler(db, cache);
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid,
            NewName = $"hijack{rid}"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        // 越权路径不触发缓存失效
        Assert.Empty(cache.InvalidatedUuids);
    }
}
