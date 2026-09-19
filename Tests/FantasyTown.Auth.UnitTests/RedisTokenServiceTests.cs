using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using StackExchange.Redis;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// P2 Redis 验收测试 - RedisTokenService
/// 需要本地 Redis (localhost:6379)，使用 DB 14 隔离
/// </summary>
public class RedisTokenServiceTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisTokenService _tokenService;
    private readonly IDatabase _db;

    public RedisTokenServiceTests()
    {
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        _db = _redis.GetDatabase(14);
        _tokenService = new RedisTokenService(_redis, TimeSpan.FromSeconds(3600), database: 14);
        Cleanup();
    }

    private void Cleanup()
    {
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: "*", database: 14);
        foreach (var key in keys) _db.KeyDelete(key);
    }

    public void Dispose()
    {
        Cleanup();
        _redis.Dispose();
    }

    [Fact]
    public async Task IssueAsync_ShouldStoreTokenInRedis()
    {
        var record = await _tokenService.IssueAsync(1, "test@test.com", "client-1", "profile-1", 0);

        Assert.False(string.IsNullOrEmpty(record.AccessToken));

        var stored = await _tokenService.ValidateAsync(record.AccessToken);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.OwnerUid);
    }

    [Fact]
    public async Task IssueAsync_SameEmail_ShouldRevokeOldToken()
    {
        var old = await _tokenService.IssueAsync(1, "test@test.com", "client-1", null, 0);
        var @new = await _tokenService.IssueAsync(1, "test@test.com", "client-2", null, 0);

        var oldStillValid = await _tokenService.ValidateAsync(old.AccessToken);
        Assert.Null(oldStillValid);

        var newValid = await _tokenService.ValidateAsync(@new.AccessToken);
        Assert.NotNull(newValid);
    }

    [Fact]
    public async Task ValidateAsync_NonExistentToken_ShouldReturnNull()
    {
        var result = await _tokenService.ValidateAsync("non-existent-token");
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ShouldReturnNewToken()
    {
        var original = await _tokenService.IssueAsync(1, "test@test.com", "client-1", "profile-1", 0);

        var refreshed = await _tokenService.RefreshAsync(original.AccessToken, "client-1", null);

        Assert.NotNull(refreshed);
        Assert.NotEqual(original.AccessToken, refreshed!.AccessToken);

        var oldStillValid = await _tokenService.ValidateAsync(original.AccessToken);
        Assert.Null(oldStillValid);
    }

    [Fact]
    public async Task RefreshAsync_WrongClientToken_ShouldReturnNull()
    {
        var original = await _tokenService.IssueAsync(1, "test@test.com", "client-1", null, 0);

        var refreshed = await _tokenService.RefreshAsync(original.AccessToken, "wrong-client", null);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task RefreshAsync_NonExistentToken_ShouldReturnNull()
    {
        var result = await _tokenService.RefreshAsync("non-existent", "client-1", null);
        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldRemoveAllTokensForEmail()
    {
        await _tokenService.IssueAsync(1, "test@test.com", "c1", null, 0);
        await _tokenService.IssueAsync(1, "test@test.com", "c2", null, 0);

        await _tokenService.RevokeAllAsync("test@test.com");

        var idKey = _db.StringGet("ID:test@test.com");
        Assert.True(idKey.IsNullOrEmpty);
    }

    [Fact]
    public async Task RevokeByTokenAsync_ShouldRemoveSpecificToken()
    {
        var record = await _tokenService.IssueAsync(1, "test@test.com", "c1", null, 0);

        await _tokenService.RevokeByTokenAsync(record.AccessToken);

        var result = await _tokenService.ValidateAsync(record.AccessToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task IssueAsync_ShouldSetCorrectTtl()
    {
        var record = await _tokenService.IssueAsync(1, "test@test.com", "c1", null, 0);

        var ttl = await _db.KeyTimeToLiveAsync($"TOKEN:{record.AccessToken}");
        Assert.NotNull(ttl);
        Assert.True(ttl!.Value.TotalSeconds > 3500);
        Assert.True(ttl.Value.TotalSeconds <= 3600);
    }
}
