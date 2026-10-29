// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// IPlayerDirectory 实现测试（H5 模块纪律边界：软删可见性、active 封禁联查、UUID 条件分配）。
/// </summary>
[Trait("Category", "Integration")]
[Collection("MariaDB")]
public class PlayerDirectoryIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task FindByUuid_CarriesActiveBanUntil()
    {
        using var db = CreateContext();
        var owner = new User
        {
            Email = $"dir-owner-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();
        var until = DateTime.UtcNow.AddDays(2);
        var player = new Player { Uid = owner.Uid, Name = $"dirp{Guid.NewGuid():N}"[..12], Uuid = Guid.NewGuid().ToString("N"), IsBanned = true };
        db.Players.Add(player);
        db.SaveChanges();
        db.PlayerBans.Add(new PlayerBan { Pid = player.Pid, BannedAt = DateTime.UtcNow, BannedBy = 0, BannedReason = "t", BannedUntil = until, IsActive = true });
        await db.SaveChangesAsync();

        var directory = new PlayerDirectory(db);
        var info = await directory.FindByUuidAsync(player.Uuid);

        Assert.NotNull(info);
        Assert.Equal(player.Name, info!.Name);
        Assert.True(info.IsBanned);
        Assert.NotNull(info.BannedUntil);
        Assert.True((info.BannedUntil!.Value.UtcDateTime - until).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task FindAccountByEmail_SoftDeletedUser_IsInvisible()
    {
        using var db = CreateContext();
        var user = new User
        {
            Email = $"dir-del-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            IsDeleted = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        db.SaveChanges();

        var directory = new PlayerDirectory(db);
        Assert.Null(await directory.FindAccountByEmailAsync(user.Email));
    }

    [Fact]
    public async Task AssignUuid_OnlyFillsEmptyUuidOnce()
    {
        using var db = CreateContext();
        var owner = new User
        {
            Email = $"dir-assign-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();
        var player = new Player { Uid = owner.Uid, Name = $"dira{Guid.NewGuid():N}"[..12], Uuid = "" };
        db.Players.Add(player);
        db.SaveChanges();

        var directory = new PlayerDirectory(db);
        var uuid = Guid.NewGuid().ToString("N");

        Assert.True(await directory.AssignUuidAsync(player.Pid, uuid));
        Assert.False(await directory.AssignUuidAsync(player.Pid, Guid.NewGuid().ToString("N"))); // 已分配：条件更新 0 行
        Assert.Equal(uuid, (await db.Players.AsNoTracking().FirstAsync(p => p.Pid == player.Pid)).Uuid);
    }

    [Fact]
    public async Task GetProfilesByUser_ReturnsPlayersOfUser()
    {
        // 当前冻结 schema：players.Uid UNIQUE（一用户一角色）；多角色为后续演进
        using var db = CreateContext();
        var owner = new User
        {
            Email = $"dir-profiles-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();
        var uuid = Guid.NewGuid().ToString("N");
        db.Players.Add(new Player { Uid = owner.Uid, Name = $"dirb{Guid.NewGuid():N}"[..12], Uuid = uuid });
        db.SaveChanges();

        var directory = new PlayerDirectory(db);
        var profiles = await directory.GetProfilesByUserAsync(owner.Uid);

        var profile = Assert.Single(profiles);
        Assert.Equal(uuid, profile.Uuid);
        Assert.Equal(owner.Uid, profile.Uid);
    }
}
