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

        var handler = new ResetPlayerTextureHandler(db, new RecordingPlayerCacheInvalidation());
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

        var handler = new ResetPlayerTextureHandler(db, new RecordingPlayerCacheInvalidation());
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

        var handler = new ResetPlayerTextureHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task Rename_Success_UpdatesNameKeepsUuidAndAudits()
    {
        using var db = CreateInMemoryDbContext();
        var (ownerUid, pid) = SeedPlayer(db, "old_name_1");
        var player = db.Players.First(p => p.Pid == pid);
        var originalUuid = player.Uuid;
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new RenamePlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            NewName = "new_name_2"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("old_name_1", result.OldName);
        var reloaded = db.Players.First(p => p.Pid == pid);
        Assert.Equal("new_name_2", reloaded.Name);
        Assert.Equal(originalUuid, reloaded.Uuid); // 映射稳定：uuid 不变
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "player-rename" && l.SubjectId == ownerUid);
    }

    [Theory]
    [InlineData("ab")]           // 过短
    [InlineData("a_name_too_long_for_the_rules")] // 过长
    [InlineData("bad name")]     // 非法字符（空格）
    [InlineData("名字!")]         // 符号非法（CJK 字母本身合法，与注册规则一致）
    public async Task Rename_InvalidName_IsRejected(string newName)
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "valid_orig");
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new RenamePlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            NewName = newName
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("角色名不符合规范（3-16 位字母/数字/下划线）", result.ErrorMessage);
    }

    [Fact]
    public async Task Rename_SameName_IsRejected()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "same_name");
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new RenamePlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            NewName = "same_name"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("角色名未变化", result.ErrorMessage);
    }

    [Fact]
    public async Task Rename_Moderator_IsDeniedOwnerTier()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "tier_guard");
        var moderator = SeedAdmin(db, UserPermission.ServerModerator);

        var handler = new RenamePlayerHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = moderator.Uid,
            Pid = pid,
            NewName = "renamed_x"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无权操作", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetTexture_MissingPlayer_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var admin = SeedAdmin(db, UserPermission.ServerOwner);

        var handler = new ResetPlayerTextureHandler(db, new RecordingPlayerCacheInvalidation());
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = admin.Uid,
            Pid = 99999
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("玩家不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetTexture_Success_InvalidatesPlayerCache()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "tex_wire", tidSkin: 7);
        var admin = SeedAdmin(db, UserPermission.ServerOwner);
        var uuid = db.Players.First(p => p.Pid == pid).Uuid;
        var cache = new RecordingPlayerCacheInvalidation();

        var handler = new ResetPlayerTextureHandler(db, cache);
        var result = await handler.HandleAsync(new ResetPlayerTextureCommand
        {
            ActorUid = admin.Uid,
            Pid = pid
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(uuid, Assert.Single(cache.InvalidatedUuids));
    }

    [Fact]
    public async Task Rename_Success_InvalidatesPlayerCacheByUuid()
    {
        using var db = CreateInMemoryDbContext();
        var (_, pid) = SeedPlayer(db, "wire_old");
        var admin = SeedAdmin(db, UserPermission.ServerOwner);
        var uuid = db.Players.First(p => p.Pid == pid).Uuid;
        var cache = new RecordingPlayerCacheInvalidation();

        var handler = new RenamePlayerHandler(db, cache);
        var result = await handler.HandleAsync(new RenamePlayerCommand
        {
            ActorUid = admin.Uid,
            Pid = pid,
            NewName = "wire_new"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(uuid, Assert.Single(cache.InvalidatedUuids));
    }
}
