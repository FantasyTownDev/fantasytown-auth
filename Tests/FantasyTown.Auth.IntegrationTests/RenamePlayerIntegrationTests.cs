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

        var handler = new RenamePlayerHandler(db);
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

        var handler = new RenamePlayerHandler(db);
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid,
            NewName = $"hijack{rid}"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }
}
