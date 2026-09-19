using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class SoftDeleteUserHandlerTests
{
    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private async Task<(int uid, int pid, string name)> SeedUserAsync(AuthDbContext db)
    {
        var user = new User
        {
            Uid = 1001,
            Email = "test@test.com",
            Password = "hashed",
            Permission = UserPermission.NormalPlayer,
            RegisterAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var player = new Player
        {
            Uid = user.Uid,
            Name = "TestPlayer",
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        return (user.Uid, player.Pid, player.Name);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_SetsIsDeletedTrue()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Uid == uid);
        Assert.True(user.IsDeleted);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_ReleasesPlayerName()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, pid, originalName) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ReleasedName);
        Assert.StartsWith($"deleted_{pid}_", result.ReleasedName);
        Assert.NotEqual(originalName, result.ReleasedName);

        var player = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Uid == uid);
        Assert.Equal(result.ReleasedName, player.Name);
    }

    [Fact]
    public async Task HandleAsync_SoftDelete_WritesAuthLog()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 42
        });

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "user-soft-delete");
        Assert.NotNull(log);
        Assert.Equal(42, log.ActorId);
        Assert.Equal(uid, log.SubjectId);
        Assert.Contains("released_name=", log.Context);
    }

    [Fact]
    public async Task HandleAsync_DoubleSoftDelete_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, _) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        await handler.HandleAsync(new SoftDeleteUserCommand { TargetUid = uid, ActorUid = 1 });
        var second = await handler.HandleAsync(new SoftDeleteUserCommand { TargetUid = uid, ActorUid = 1 });

        Assert.False(second.IsSuccess);
        Assert.Contains("已软删除", second.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_NonExistentUser_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new SoftDeleteUserHandler(db);

        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 9999,
            ActorUid = 1
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ReleasedName_AllowsNewRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var (uid, _, originalName) = await SeedUserAsync(db);
        var handler = new SoftDeleteUserHandler(db);

        var deleteResult = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = uid,
            ActorUid = 1
        });

        Assert.True(deleteResult.IsSuccess);

        // 同名应可注册（原名已释放为占位符）
        var newUser = new User
        {
            Uid = 2001,
            Email = "new@test.com",
            Password = "hashed",
            Permission = UserPermission.NormalPlayer,
            RegisterAt = DateTime.UtcNow
        };
        db.Users.Add(newUser);
        var newPlayer = new Player
        {
            Uid = newUser.Uid,
            Name = originalName, // 重用原名
            Uuid = Guid.NewGuid().ToString("N")
        };
        db.Players.Add(newPlayer);
        await db.SaveChangesAsync();

        var player = await db.Players.FirstAsync(p => p.Name == originalName);
        Assert.Equal(newUser.Uid, player.Uid);
    }
}
