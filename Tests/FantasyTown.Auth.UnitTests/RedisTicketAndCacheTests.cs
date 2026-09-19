using FantasyTown.Auth.Modules.Yggdrasil.Sessions;
using StackExchange.Redis;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// P3 Redis 验收测试 - TicketService + PlayerCache
/// 需要本地 Redis (localhost:6379)，使用 DB 13 隔离
/// </summary>
public class RedisTicketServiceTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisTicketService _ticketService;

    public RedisTicketServiceTests()
    {
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        _ticketService = new RedisTicketService(_redis, database: 13);
        Cleanup();
    }

    private void Cleanup()
    {
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: "SERVER:*", database: 13);
        foreach (var key in keys) _redis.GetDatabase(13).KeyDelete(key);
    }

    public void Dispose()
    {
        Cleanup();
        _redis.Dispose();
    }

    [Fact]
    public async Task SetAsync_And_ConsumeAsync_ShouldReturnUuid()
    {
        await _ticketService.SetAsync("server-1", "uuid-123");

        var result = await _ticketService.ConsumeAsync("server-1");

        Assert.Equal("uuid-123", result);
    }

    [Fact]
    public async Task ConsumeAsync_ShouldBeOneTime()
    {
        await _ticketService.SetAsync("server-1", "uuid-123");

        var first = await _ticketService.ConsumeAsync("server-1");
        var second = await _ticketService.ConsumeAsync("server-1");

        Assert.Equal("uuid-123", first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_NonExistent_ShouldReturnNull()
    {
        var result = await _ticketService.ConsumeAsync("non-existent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Overwrites_ShouldReturnNewest()
    {
        await _ticketService.SetAsync("server-1", "uuid-old");
        await _ticketService.SetAsync("server-1", "uuid-new");

        var result = await _ticketService.ConsumeAsync("server-1");
        Assert.Equal("uuid-new", result);
    }

    [Fact]
    public async Task MultipleServers_ShouldBeIndependent()
    {
        await _ticketService.SetAsync("server-1", "uuid-1");
        await _ticketService.SetAsync("server-2", "uuid-2");

        var r1 = await _ticketService.ConsumeAsync("server-1");
        var r2 = await _ticketService.ConsumeAsync("server-2");

        Assert.Equal("uuid-1", r1);
        Assert.Equal("uuid-2", r2);
    }

    [Fact]
    public async Task SetAsync_ShouldHaveTtl()
    {
        await _ticketService.SetAsync("server-1", "uuid-1");

        var db = _redis.GetDatabase(13);
        var ttl = await db.KeyTimeToLiveAsync("SERVER:server-1");

        Assert.NotNull(ttl);
        Assert.True(ttl!.Value.TotalSeconds > 0);
        Assert.True(ttl.Value.TotalSeconds <= 60);
    }
}

/// <summary>
/// P3 Redis 验收测试 - PlayerCache
/// 使用 DB 12 隔离
/// </summary>
public class RedisPlayerCacheTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisPlayerCache _cache;

    public RedisPlayerCacheTests()
    {
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        _cache = new RedisPlayerCache(_redis, database: 12);
        Cleanup();
    }

    private void Cleanup()
    {
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: "PLAYER:*", database: 12);
        foreach (var key in keys) _redis.GetDatabase(12).KeyDelete(key);
    }

    public void Dispose()
    {
        Cleanup();
        _redis.Dispose();
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_ShouldReturnSnapshot()
    {
        var snapshot = new PlayerSnapshot
        {
            Name = "TestPlayer",
            LastModified = 1234567890,
            IsBanned = false,
            TexturesUrl = "http://example.com/texture.png"
        };

        await _cache.SetAsync("uuid-1", snapshot);
        var result = await _cache.GetAsync("uuid-1");

        Assert.NotNull(result);
        Assert.Equal("TestPlayer", result!.Name);
        Assert.Equal(1234567890, result.LastModified);
        Assert.False(result.IsBanned);
        Assert.Equal("http://example.com/texture.png", result.TexturesUrl);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ShouldReturnNull()
    {
        var result = await _cache.GetAsync("non-existent");
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_ShouldDelete()
    {
        var snapshot = new PlayerSnapshot
        {
            Name = "TestPlayer",
            LastModified = 12345,
            IsBanned = false
        };

        await _cache.SetAsync("uuid-1", snapshot);
        await _cache.RemoveAsync("uuid-1");

        var result = await _cache.GetAsync("uuid-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Overwrites_ShouldReturnNewest()
    {
        var old = new PlayerSnapshot
        {
            Name = "OldPlayer",
            LastModified = 111,
            IsBanned = false
        };
        var @new = new PlayerSnapshot
        {
            Name = "NewPlayer",
            LastModified = 222,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddDays(1)
        };

        await _cache.SetAsync("uuid-1", old);
        await _cache.SetAsync("uuid-1", @new);

        var result = await _cache.GetAsync("uuid-1");
        Assert.NotNull(result);
        Assert.Equal("NewPlayer", result!.Name);
        Assert.True(result.IsBanned);
    }

    [Fact]
    public async Task SetAsync_ShouldHaveTtl()
    {
        var snapshot = new PlayerSnapshot
        {
            Name = "TestPlayer",
            LastModified = 12345,
            IsBanned = false
        };

        await _cache.SetAsync("uuid-1", snapshot);

        var db = _redis.GetDatabase(12);
        var ttl = await db.KeyTimeToLiveAsync("PLAYER:uuid-1");

        Assert.NotNull(ttl);
        Assert.True(ttl!.Value.TotalSeconds > 0);
        Assert.True(ttl.Value.TotalSeconds <= 60);
    }

    [Fact]
    public async Task Snapshot_StoresBanInfo()
    {
        var snapshot = new PlayerSnapshot
        {
            Name = "BannedPlayer",
            LastModified = 12345,
            IsBanned = true,
            BannedUntil = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            TexturesSignature = "sig123"
        };

        await _cache.SetAsync("uuid-1", snapshot);
        var result = await _cache.GetAsync("uuid-1");

        Assert.NotNull(result);
        Assert.True(result!.IsBanned);
        Assert.NotNull(result.BannedUntil);
        Assert.Equal("sig123", result.TexturesSignature);
    }
}
