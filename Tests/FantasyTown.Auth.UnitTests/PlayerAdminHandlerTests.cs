using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// 玩家域管理命令单测（InMemory）：重置皮肤、改名。封禁/解封走 MariaDB 集成测试。
/// </summary>
public class PlayerAdminHandlerTests
{
    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    internal static (int OwnerUid, int Pid) SeedPlayer(AuthDbContext db, string name, UserPermission ownerPermission = UserPermission.NormalPlayer, int? tidSkin = null)
    {
        var owner = new User
        {
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            Permission = ownerPermission,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(owner);
        db.SaveChanges();

        var player = new Player
        {
            Uid = owner.Uid,
            Name = name,
            Uuid = Guid.NewGuid().ToString("N"),
            TidSkin = tidSkin
        };
        db.Players.Add(player);
        db.SaveChanges();
        return (owner.Uid, player.Pid);
    }

    internal static User SeedAdmin(AuthDbContext db, UserPermission permission)
    {
        var admin = new User
        {
            Email = $"admin-{Guid.NewGuid():N}@example.com",
            Password = "hashed_x",
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(admin);
        db.SaveChanges();
        return admin;
    }

    [Fact]
    public async Task ResetTexture_WithSkin_ClearsAndAudits()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "tex_player", tidSkin: 42);
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new ResetPlayerTextureHandler(db);
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = admin.Uid,
            Pid = pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HadSkin);
        Assert.Null(db.Players.First(p => p.Pid == pid).TidSkin);
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "texture-reset" && l.Context!.Contains("had_skin=true"));
    }

    [Fact]
    public async Task ResetTexture_WithoutSkin_ReportsHadSkinFalse()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "tex_plain");
        var admin = SeedAdmin(db, UserPermission.ServerModerator);

        var handler = new ResetPlayerTextureHandler(db);
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = admin.Uid,
            Pid = pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.HadSkin);
    }

    [Fact]
    public async Task ResetTexture_ModeratorOnOwnersPlayer_Denies()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "tex_guarded", ownerPermission: UserPermission.ServerOwner);
        var moderator = SeedAdmin(db, UserPermission.ServerModerator);

        var handler = new ResetPlayerTextureHandler(db);
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetTexture_MissingPlayer_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new ResetPlayerTextureHandler(db);
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = admin.Uid,
            Pid = 99999
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("玩家不存在", result.ErrorMessage);
    }
}
