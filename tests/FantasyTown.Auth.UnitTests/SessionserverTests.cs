using System.Text.Json;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Sessions;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

#region TicketService Tests

public class InMemoryTicketServiceTests
{
    private readonly InMemoryTicketService _ticketService = new();

    [Fact]
    public async Task SetAsync_StoresTicket()
    {
        await _ticketService.SetAsync("server1", "uuid-abc");
        var result = await _ticketService.ConsumeAsync("server1");

        Assert.Equal("uuid-abc", result);
    }

    [Fact]
    public async Task ConsumeAsync_ReturnsUuid_ThenRemoves()
    {
        await _ticketService.SetAsync("server1", "uuid-abc");

        var first = await _ticketService.ConsumeAsync("server1");
        var second = await _ticketService.ConsumeAsync("server1");

        Assert.Equal("uuid-abc", first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_Nonexistent_ReturnsNull()
    {
        var result = await _ticketService.ConsumeAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingTicket()
    {
        await _ticketService.SetAsync("server1", "uuid-old");
        await _ticketService.SetAsync("server1", "uuid-new");

        var result = await _ticketService.ConsumeAsync("server1");
        Assert.Equal("uuid-new", result);
    }

    [Fact]
    public async Task MultipleServers_IndependentTickets()
    {
        await _ticketService.SetAsync("server1", "uuid-1");
        await _ticketService.SetAsync("server2", "uuid-2");

        Assert.Equal("uuid-1", await _ticketService.ConsumeAsync("server1"));
        Assert.Equal("uuid-2", await _ticketService.ConsumeAsync("server2"));
    }
}

#endregion

#region PlayerCache Tests

public class InMemoryPlayerCacheTests
{
    private readonly InMemoryPlayerCache _cache = new();

    private PlayerSnapshot CreateSnapshot(string name = "Player1", bool isBanned = false) => new()
    {
        Name = name,
        LastModified = DateTime.UtcNow.Ticks,
        IsBanned = isBanned,
        BannedUntil = null
    };

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotCached()
    {
        var result = await _cache.GetAsync("uuid-abc");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_StoresSnapshot()
    {
        var snapshot = CreateSnapshot();
        await _cache.SetAsync("uuid-abc", snapshot);

        var result = await _cache.GetAsync("uuid-abc");
        Assert.NotNull(result);
        Assert.Equal("Player1", result!.Name);
    }

    [Fact]
    public async Task RemoveAsync_DeletesSnapshot()
    {
        await _cache.SetAsync("uuid-abc", CreateSnapshot());
        await _cache.RemoveAsync("uuid-abc");

        var result = await _cache.GetAsync("uuid-abc");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_OverwritesExisting()
    {
        await _cache.SetAsync("uuid-abc", CreateSnapshot("Old"));
        await _cache.SetAsync("uuid-abc", CreateSnapshot("New"));

        var result = await _cache.GetAsync("uuid-abc");
        Assert.Equal("New", result!.Name);
    }

    [Fact]
    public async Task Snapshot_StoresBanInfo()
    {
        var snapshot = CreateSnapshot(isBanned: true) with
        {
            BannedUntil = DateTime.UtcNow.AddDays(7)
        };
        await _cache.SetAsync("uuid-abc", snapshot);

        var result = await _cache.GetAsync("uuid-abc");
        Assert.True(result!.IsBanned);
        Assert.NotNull(result.BannedUntil);
    }

    [Fact]
    public async Task Snapshot_StoresTexturesUrl()
    {
        var snapshot = CreateSnapshot() with
        {
            TexturesUrl = "https://example.com/skin.png"
        };
        await _cache.SetAsync("uuid-abc", snapshot);

        var result = await _cache.GetAsync("uuid-abc");
        Assert.Equal("https://example.com/skin.png", result!.TexturesUrl);
    }
}

#endregion

#region SigningService Tests

public class InMemorySigningServiceTests
{
    private readonly InMemorySigningService _signingService = new();

    [Fact]
    public void Sign_ReturnsSignature()
    {
        var sig = _signingService.Sign("test payload");
        Assert.NotNull(sig);
        Assert.NotEmpty(sig);
    }

    [Fact]
    public void Verify_AlwaysReturnsTrue()
    {
        Assert.True(_signingService.Verify("payload", "sig"));
    }

    [Fact]
    public void GetPublicKeyPem_ReturnsPemFormat()
    {
        var key = _signingService.GetPublicKeyPem();
        Assert.NotNull(key);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", key);
        Assert.EndsWith("-----END PUBLIC KEY-----", key);
    }
}

#endregion

#region JoinHandler Tests

public class JoinHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryTokenService _tokenService;
    private readonly InMemoryTicketService _ticketService;
    private readonly JoinHandler _handler;

    public JoinHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _tokenService = new InMemoryTokenService();
        _ticketService = new InMemoryTicketService();
        _handler = new JoinHandler(_db, _tokenService, _ticketService);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(string token, string profileId)> IssueToken(int uid = 1, string profileId = "abc123")
    {
        var token = await _tokenService.IssueAsync(uid, "test@test.com", "client-token", profileId, 0);
        return (token.AccessToken, profileId);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_ReturnsSuccess()
    {
        var (token, profileId) = await IssueToken();
        var result = await _handler.HandleAsync(token, profileId, "server-123");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_CreatesTicket()
    {
        var (token, profileId) = await IssueToken();
        await _handler.HandleAsync(token, profileId, "server-123");

        var ticket = await _ticketService.ConsumeAsync("server-123");
        Assert.Equal(profileId, ticket);
    }

    [Fact]
    public async Task HandleAsync_InvalidToken_ReturnsInvalid()
    {
        var result = await _handler.HandleAsync("invalid-token", "abc", "server-123");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_ProfileIdMismatch_ReturnsInvalid()
    {
        var (token, _) = await IssueToken(profileId: "abc123");
        var result = await _handler.HandleAsync(token, "wrong-profile", "server-123");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_MultipleServers_IndependentTickets()
    {
        var (token, profileId) = await IssueToken();

        await _handler.HandleAsync(token, profileId, "server-1");
        await _handler.HandleAsync(token, profileId, "server-2");

        Assert.Equal(profileId, await _ticketService.ConsumeAsync("server-1"));
        Assert.Equal(profileId, await _ticketService.ConsumeAsync("server-2"));
    }
}

#endregion

#region HasJoinedHandler Tests

public class HasJoinedHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryTicketService _ticketService;
    private readonly InMemoryPlayerCache _playerCache;
    private readonly InMemorySigningService _signingService;
    private readonly HasJoinedHandler _handler;

    public HasJoinedHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _ticketService = new InMemoryTicketService();
        _playerCache = new InMemoryPlayerCache();
        _signingService = new InMemorySigningService();
        _handler = new HasJoinedHandler(_db, _ticketService, _playerCache, _signingService, new YggOptions());
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedPlayer(string uuid = "abc123def456abc123def456abc123de", string name = "TestPlayer")
    {
        var user = new User
        {
            Email = "test@test.com",
            Password = "hashed_pw",
            Ip = "127.0.0.1",
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            Verified = false,
            IsDeleted = false,
            RegisterAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.Players.Add(new Player
        {
            Uid = user.Uid,
            Name = name,
            Uuid = uuid,
            IsBanned = false,
            LastModified = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_NoTicket_ReturnsNotFound()
    {
        var result = await _handler.HandleAsync("TestPlayer", "server-123");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_ValidTicket_ReturnsProfile()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);
        await _ticketService.SetAsync("server-123", uuid);

        var result = await _handler.HandleAsync("TestPlayer", "server-123");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Profile);
        Assert.Equal(uuid, result.Profile!.Uuid);
        Assert.Equal("TestPlayer", result.Profile.Name);
    }

    [Fact]
    public async Task HandleAsync_TicketConsumed_OneTime()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);
        await _ticketService.SetAsync("server-123", uuid);

        var r1 = await _handler.HandleAsync("TestPlayer", "server-123");
        var r2 = await _handler.HandleAsync("TestPlayer", "server-123");

        Assert.True(r1.IsValid);
        Assert.False(r2.IsValid);
    }

    [Fact]
    public async Task HandleAsync_CacheHit_SkipsDb()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await _ticketService.SetAsync("server-123", uuid);

        // 预热缓存
        await _playerCache.SetAsync(uuid, new PlayerSnapshot
        {
            Name = "CachedPlayer",
            LastModified = DateTime.UtcNow.Ticks,
            IsBanned = false
        });

        var result = await _handler.HandleAsync("CachedPlayer", "server-123");

        Assert.True(result.IsValid);
        Assert.Equal("CachedPlayer", result.Profile!.Name);
    }

    [Fact]
    public async Task HandleAsync_BannedPlayer_ReturnsNotFound()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await _ticketService.SetAsync("server-123", uuid);

        await _playerCache.SetAsync(uuid, new PlayerSnapshot
        {
            Name = "BannedPlayer",
            LastModified = DateTime.UtcNow.Ticks,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddDays(30)
        });

        var result = await _handler.HandleAsync("BannedPlayer", "server-123");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_ProfileContainsTexturesProperty()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);
        await _ticketService.SetAsync("server-123", uuid);

        var result = await _handler.HandleAsync("TestPlayer", "server-123");

        Assert.True(result.IsValid);
        Assert.Single(result.Profile!.Properties);
        Assert.Equal("textures", result.Profile.Properties[0].Name);
        Assert.NotEmpty(result.Profile.Properties[0].Value);
        Assert.NotEmpty(result.Profile.Properties[0].Signature);
    }

    [Fact]
    public async Task HandleAsync_TexturesValue_IsValidBase64()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);
        await _ticketService.SetAsync("server-123", uuid);

        var result = await _handler.HandleAsync("TestPlayer", "server-123");

        var valueBytes = Convert.FromBase64String(result.Profile!.Properties[0].Value);
        var payloadJson = System.Text.Encoding.UTF8.GetString(valueBytes);
        using var doc = JsonDocument.Parse(payloadJson);

        Assert.Equal(uuid, doc.RootElement.GetProperty("profileId").GetString());
        Assert.Equal("TestPlayer", doc.RootElement.GetProperty("profileName").GetString());
    }
}

#endregion

#region ProfileHandler Tests

public class ProfileHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryPlayerCache _playerCache;
    private readonly InMemorySigningService _signingService;
    private readonly ProfileHandler _handler;

    public ProfileHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _playerCache = new InMemoryPlayerCache();
        _signingService = new InMemorySigningService();
        _handler = new ProfileHandler(_db, _playerCache, _signingService, new YggOptions());
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedPlayer(string uuid = "abc123def456abc123def456abc123de", string name = "TestPlayer")
    {
        var user = new User
        {
            Email = "test@test.com",
            Password = "hashed_pw",
            Ip = "127.0.0.1",
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            Verified = false,
            IsDeleted = false,
            RegisterAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.Players.Add(new Player
        {
            Uid = user.Uid,
            Name = name,
            Uuid = uuid,
            IsBanned = false,
            LastModified = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_NonexistentUuid_ReturnsNotFound()
    {
        var result = await _handler.HandleAsync("nonexistent-uuid");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_ValidUuid_ReturnsProfile()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);

        var result = await _handler.HandleAsync(uuid);

        Assert.True(result.IsValid);
        Assert.Equal(uuid, result.Profile!.Uuid);
        Assert.Equal("TestPlayer", result.Profile.Name);
        Assert.True(result.LastModified > 0);
    }

    [Fact]
    public async Task HandleAsync_CacheHit_SkipsDb()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await _playerCache.SetAsync(uuid, new PlayerSnapshot
        {
            Name = "CachedPlayer",
            LastModified = 1234567890,
            IsBanned = false
        });

        var result = await _handler.HandleAsync(uuid);

        Assert.True(result.IsValid);
        Assert.Equal("CachedPlayer", result.Profile!.Name);
        Assert.Equal(1234567890, result.LastModified);
    }

    [Fact]
    public async Task HandleAsync_CachesResult()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);

        await _handler.HandleAsync(uuid);
        var cached = await _playerCache.GetAsync(uuid);

        Assert.NotNull(cached);
        Assert.Equal("TestPlayer", cached!.Name);
    }

    [Fact]
    public async Task HandleAsync_Profile_HasTexturesSignature()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await SeedPlayer(uuid);

        var result = await _handler.HandleAsync(uuid);

        Assert.True(result.IsValid);
        Assert.Single(result.Profile!.Properties);
        Assert.Equal("textures", result.Profile.Properties[0].Name);
        Assert.NotEmpty(result.Profile.Properties[0].Signature);
    }

    [Fact]
    public async Task HandleAsync_BannedPlayer_ProfileShowsBanStatus()
    {
        var uuid = "abc123def456abc123def456abc123de";
        await _playerCache.SetAsync(uuid, new PlayerSnapshot
        {
            Name = "BannedPlayer",
            LastModified = DateTime.UtcNow.Ticks,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddDays(7)
        });

        var result = await _handler.HandleAsync(uuid);

        Assert.True(result.IsValid);
        Assert.Equal("BannedPlayer", result.Profile!.Name);
    }
}

#endregion
