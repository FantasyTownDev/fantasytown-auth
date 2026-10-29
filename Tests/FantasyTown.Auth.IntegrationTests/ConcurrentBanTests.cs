// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 并发封禁测试（需要 MariaDB 或 Docker）
/// </summary>
[Trait("Category", "Integration")]
[Collection("MariaDB")]
public class ConcurrentBanTests : IAsyncLifetime
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

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
    }

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString))
            .Options;
        return new AuthDbContext(options);
    }

    // P4 提前测试：V5 冻结设计（设计方案 §5.3/7.5）规定"每玩家至多一条 active"由
    // 封禁命令层条件更新闸门保证（MySQL 无部分唯一索引，明确不依赖 DB 约束）。
    // 封禁命令属 P4 管理域尚未实现，且本用例直接裸插 PlayerBan 实体、不经过闸门，
    // 按现状无法验证设计不变量。P4 时须按实施指导 §5 测试矩阵重写为：
    // 并发双封 + 过期续封混合场景，经封禁命令执行后终态断言 COUNT(active) <= 1。
    [Fact(Skip = "P4 rewrite target: must exercise ban command conditional gate (V5 §5.3/7.5), raw EF inserts bypass the gate by design")]
    public async Task ConcurrentBan_ShouldHaveAtMostOneActiveBan()
    {
        const int concurrentCount = 20;
        using var context = CreateContext();

        // 每次运行唯一邮箱/角色名：避免与注册测试类（并行）及历史残留冲突
        var rid = Guid.NewGuid().ToString("N")[..8];
        var user = new User
        {
            Email = $"banhost-{rid}@example.com",
            Password = "hashedpassword",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var player = new Player
        {
            Uid = user.Uid,
            Name = $"BanPlayer-{rid}",
            Uuid = Guid.NewGuid().ToString("N").ToUpper()
        };
        context.Players.Add(player);
        await context.SaveChangesAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var ban = new PlayerBan
                {
                    Pid = player.Pid,
                    BannedAt = DateTime.UtcNow,
                    BannedBy = user.Uid,
                    BannedReason = "Test ban",
                    BannedUntil = DateTime.UtcNow.AddDays(1),
                    IsActive = true
                };
                ctx.PlayerBans.Add(ban);
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch
                {
                    // Expected for concurrent conflicts
                }
            }));
        }

        await Task.WhenAll(tasks);

        using var verifyContext = CreateContext();
        var activeBans = await verifyContext.PlayerBans
            .Where(b => b.Pid == player.Pid && b.IsActive && b.BannedUntil > DateTime.UtcNow)
            .CountAsync();

        Assert.True(activeBans <= 1, $"Expected at most 1 active ban, got {activeBans}");
    }
}
