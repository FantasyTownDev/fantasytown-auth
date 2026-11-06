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
/// 玩家解封命令集成测试（§5.3 对称语义 + 防错 #7：0 行 = 幂等成功）。
/// </summary>
[Trait("Category", "Integration")]
[Collection("MariaDB")]
public class UnbanPlayerIntegrationTests : IAsyncLifetime
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

    private static (int OwnerUid, int Pid) SeedBannedPlayer(AuthDbContext db, string rid, string prefix)
    {
        var owner = new User
        {
            Email = $"{prefix}-owner-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();

        var player = new Player
        {
            Uid = owner.Uid,
            Name = $"{prefix}{rid}",
            Uuid = Guid.NewGuid().ToString("N"),
            IsBanned = true
        };
        db.Players.Add(player);
        db.SaveChanges();

        db.PlayerBans.Add(new PlayerBan
        {
            Pid = player.Pid,
            BannedAt = DateTime.UtcNow.AddDays(-1),
            BannedBy = 0,
            BannedReason = "seed",
            BannedUntil = null,
            IsActive = true
        });
        db.SaveChanges();
        return (owner.Uid, player.Pid);
    }

    private static User SeedOwnerAdmin(AuthDbContext db, string rid)
    {
        var admin = new User
        {
            Email = $"admin-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.ServerOwner,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(admin);
        db.SaveChanges();
        return admin;
    }

    [Fact]
    public async Task Unban_BannedPlayer_ClearsFlagAndActiveRecord()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedBannedPlayer(db, rid, "unb");
        var admin = SeedOwnerAdmin(db, rid);

        var handler = new UnbanPlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new UnbanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.WasBanned);
        Assert.Equal(1, result.PriorBanCount);
        Assert.False((await db.Players.AsNoTracking().FirstAsync(p => p.Pid == pid)).IsBanned);
        Assert.Equal(0, await db.PlayerBans.CountAsync(b => b.Pid == pid && b.IsActive));
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "player-unban" && l.SubjectId == ownerUid);
    }

    [Fact]
    public async Task Unban_BannedPlayer_InvalidatesPlayerCache()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (_, pid) = SeedBannedPlayer(db, rid, "unw");
        var admin = SeedOwnerAdmin(db, rid);
        var uuid = (await db.Players.AsNoTracking().FirstAsync(p => p.Pid == pid)).Uuid;
        var cache = new RecordingPlayerCacheInvalidation();

        var handler = new UnbanPlayerHandler(db, cache);
        var result = await handler.HandleAsync(new UnbanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(uuid, Assert.Single(cache.InvalidatedUuids));
    }

    [Fact]
    public async Task Unban_BannedPlayer_PublishesBanSignalOnlyOnce()
    {
        // BAN_SIGNAL：实际解封才广播一次；幂等 0 行（无状态变化）不发
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedBannedPlayer(db, rid, "uns");
        var admin = SeedOwnerAdmin(db, rid);
        var publisher = new RecordingBanSignalPublisher();

        var handler = new UnbanPlayerHandler(db, new RecordingPlayerCacheInvalidation(), publisher);
        var result = await handler.HandleAsync(new UnbanPlayerCommand { ActorUid = admin.Uid, Pid = pid });
        var second = await handler.HandleAsync(new UnbanPlayerCommand { ActorUid = admin.Uid, Pid = pid });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.WasBanned);
        Assert.False(second.WasBanned);
        var signal = Assert.Single(publisher.Published);
        Assert.Equal("unban", signal.Action);
        Assert.Equal("player", signal.Scope);
        Assert.Equal(pid, signal.Pid);
        Assert.Equal(ownerUid, signal.Uid);
        Assert.Equal(admin.Uid, signal.OperatorUid);
        Assert.False(string.IsNullOrEmpty(signal.Uuid));
    }

    [Fact]
    public async Task Unban_NotBannedPlayer_IsIdempotentSuccess()
    {
        // 防错 #7：0 行按幂等成功处理（WasBanned=false 供 TempData 提示）
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var owner = new User
        {
            Email = $"plain-owner-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();
        var player = new Player { Uid = owner.Uid, Name = $"unp{rid}", Uuid = Guid.NewGuid().ToString("N") };
        db.Players.Add(player);
        db.SaveChanges();
        var admin = SeedOwnerAdmin(db, rid);

        var handler = new UnbanPlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new UnbanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = player.Pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.WasBanned);
    }

    [Fact]
    public async Task Unban_ModeratorCannotUnbanOwnersPlayer_Denies()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedBannedPlayer(db, rid, "und");
        db.Users.First(u => u.Uid == ownerUid).Permission = UserPermission.ServerOwner;
        await db.SaveChangesAsync();
        var moderator = new User
        {
            Email = $"mod-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.ServerModerator,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(moderator);
        db.SaveChanges();

        var handler = new UnbanPlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new UnbanPlayerCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        // 保持封禁状态
        Assert.True((await db.Players.AsNoTracking().FirstAsync(p => p.Pid == pid)).IsBanned);
    }
}
