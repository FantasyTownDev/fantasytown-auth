using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;

namespace FantasyTown.Auth.UnitTests;

public class PermissionSnapshotTests
{
    [Fact]
    public async Task GetAsync_NonexistentUser_ReturnsNull()
    {
        var snapshot = new InMemoryPermissionSnapshot();
        var result = await snapshot.GetAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_ReturnsSnapshot()
    {
        var snapshot = new InMemoryPermissionSnapshot();
        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.ServerModerator,
            IsBanned = false,
            BannedUntil = null,
            BannedReason = null,
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(1, permission);
        var result = await snapshot.GetAsync(1);

        Assert.NotNull(result);
        Assert.Equal(UserPermission.ServerModerator, result.Permission);
        Assert.False(result.IsBanned);
    }

    [Fact]
    public async Task RemoveAsync_RemovesSnapshot()
    {
        var snapshot = new InMemoryPermissionSnapshot();
        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(1, permission);
        await snapshot.RemoveAsync(1);
        var result = await snapshot.GetAsync(1);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingSnapshot()
    {
        var snapshot = new InMemoryPermissionSnapshot();
        
        await snapshot.SetAsync(1, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        });

        await snapshot.SetAsync(1, new PermissionSnapshot
        {
            Permission = UserPermission.ServerOwner,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddHours(1),
            BannedReason = "违规",
            CreatedAt = DateTime.UtcNow
        });

        var result = await snapshot.GetAsync(1);

        Assert.NotNull(result);
        Assert.Equal(UserPermission.ServerOwner, result.Permission);
        Assert.True(result.IsBanned);
        Assert.Equal("违规", result.BannedReason);
    }

    [Fact]
    public async Task Snapshot_PermanentBan_HasNullBannedUntil()
    {
        var snapshot = new InMemoryPermissionSnapshot();
        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = null,
            BannedReason = "永久封禁",
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(1, permission);
        var result = await snapshot.GetAsync(1);

        Assert.NotNull(result);
        Assert.True(result.IsBanned);
        Assert.Null(result.BannedUntil);
        Assert.Equal("永久封禁", result.BannedReason);
    }

    [Fact]
    public async Task Snapshot_TemporaryBan_HasBannedUntil()
    {
        var bannedUntil = DateTime.UtcNow.AddHours(2);
        var snapshot = new InMemoryPermissionSnapshot();
        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = bannedUntil,
            BannedReason = "临时封禁",
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(1, permission);
        var result = await snapshot.GetAsync(1);

        Assert.NotNull(result);
        Assert.True(result.IsBanned);
        Assert.Equal(bannedUntil, result.BannedUntil);
    }
}
