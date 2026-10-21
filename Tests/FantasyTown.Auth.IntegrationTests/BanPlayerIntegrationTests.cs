using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 玩家封禁命令集成测试（V5 §5.3/§7.5：条件更新闸门 + 每玩家至多一条 active）。
/// 本地经 appsettings.IntegrationTests.json 连 MariaDB；CI 有 Docker 时用 Testcontainers。
/// </summary>
[Trait("Category", "Integration")]
public class BanPlayerIntegrationTests : IAsyncLifetime
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

    private static (int ActorUid, int Pid) SeedOwnerWithPlayer(AuthDbContext db, string rid, string namePrefix)
    {
        var owner = new User
        {
            Email = $"{namePrefix}-owner-{rid}@example.com",
            Password = "hashed_x",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();

        var player = new Player
        {
            Uid = owner.Uid,
            Name = $"{namePrefix}{rid}",
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(player);
        db.SaveChanges();
        return (owner.Uid, player.Pid);
    }

    private static User SeedAdmin(AuthDbContext db, string rid, UserPermission permission)
    {
        var admin = new User
        {
            Email = $"admin-{rid}@example.com",
            Password = "hashed_x",
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(admin);
        db.SaveChanges();
        return admin;
    }

    [Fact]
    public async Task Ban_Succeeds_WritesActiveRecordAndAudit()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedOwnerWithPlayer(db, rid, "pbn");
        var admin = SeedAdmin(db, rid, UserPermission.ServerOwner);

        var handler = new BanPlayerHandler(db);
        var result = await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            Reason = "test ban"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // ExecuteUpdateAsync 绕过变更跟踪：断言须无跟踪读库
        Assert.True((await db.Players.AsNoTracking().FirstAsync(p => p.Pid == pid)).IsBanned);
        Assert.Equal(1, await db.PlayerBans.CountAsync(b => b.Pid == pid && b.IsActive));
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "player-ban" && l.SubjectId == ownerUid);
    }

    [Fact]
    public async Task Ban_Twice_ReturnsConflictWithSingleActiveRecord()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (_, pid) = SeedOwnerWithPlayer(db, rid, "pbt");
        var admin = SeedAdmin(db, rid, UserPermission.ServerOwner);
        var handler = new BanPlayerHandler(db);

        var until = DateTime.UtcNow.AddDays(3);
        Assert.True((await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            Reason = "first",
            Until = until
        })).IsSuccess, "首次封禁应成功");

        var second = await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            Reason = "second",
            Until = until.AddDays(3)
        });

        Assert.False(second.IsSuccess);
        Assert.Equal(until, second.CurrentBannedUntil);
        Assert.Equal("first", second.CurrentBannedReason);
        // 唯一 active 不变量（V4 R2）
        Assert.Equal(1, await db.PlayerBans.CountAsync(b => b.Pid == pid && b.IsActive));
    }

    [Fact]
    public async Task Ban_ExpiredTemporaryBan_IsTakenOverInOneStep()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (_, pid) = SeedOwnerWithPlayer(db, rid, "pbe");
        var admin = SeedAdmin(db, rid, UserPermission.ServerOwner);

        // 过期未归位的临时封禁（N3 语义：闸门谓词放行续封/改判）
        db.Players.First(p => p.Pid == pid).IsBanned = true;
        db.PlayerBans.Add(new PlayerBan
        {
            Pid = pid,
            BannedAt = DateTime.UtcNow.AddDays(-10),
            BannedBy = admin.Uid,
            BannedReason = "expired",
            BannedUntil = DateTime.UtcNow.AddDays(-1),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var handler = new BanPlayerHandler(db);
        var result = await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            Reason = "reban after expiry"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, await db.PlayerBans.CountAsync(b => b.Pid == pid && b.IsActive));
        Assert.Equal("reban after expiry",
            (await db.PlayerBans.FirstAsync(b => b.Pid == pid && b.IsActive)).BannedReason);
    }

    [Fact]
    public async Task Ban_ModeratorCannotBanOwnersPlayer_DeniesWithAudit()
    {
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedOwnerWithPlayer(db, rid, "pbm");
        // 属主为服主，操作者为协管：CanManage(1,2) = false
        db.Users.First(u => u.Uid == ownerUid).Permission = UserPermission.ServerOwner;
        await db.SaveChangesAsync();
        var moderator = SeedAdmin(db, rid, UserPermission.ServerModerator);

        var handler = new BanPlayerHandler(db);
        var result = await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
        Assert.False((await db.Players.FirstAsync(p => p.Pid == pid)).IsBanned);
        Assert.Contains(db.AuthLogs.Local, l =>
            l.Action == "admin-denied" && l.ActorId == moderator.Uid && l.SubjectId == ownerUid);
    }

    [Fact]
    public async Task Ban_OwnPlayer_IsDeniedByNotSelf()
    {
        // §7.5 时序锚点：NotSelf 对属主——任何人不能管理自己名下的玩家
        using var db = CreateContext();
        var rid = Guid.NewGuid().ToString("N")[..8];
        var (ownerUid, pid) = SeedOwnerWithPlayer(db, rid, "pbs");
        db.Users.First(u => u.Uid == ownerUid).Permission = UserPermission.ServerOwner;
        await db.SaveChangesAsync();

        var handler = new BanPlayerHandler(db);
        var result = await handler.HandleAsync(new BanPlayerCommand
        {
            ActorUid = ownerUid,
            Pid = pid
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }
}
