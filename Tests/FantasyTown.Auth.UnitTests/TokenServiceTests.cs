using FantasyTown.Auth.Modules.Yggdrasil.Authserver;

namespace FantasyTown.Auth.UnitTests;

public class TokenServiceTests
{
    [Fact]
    public async Task IssueAsync_ShouldReturnValidToken()
    {
        var service = new InMemoryTokenService();
        var token = await service.IssueAsync(uid: 1, email: "test@test.com", clientToken: "client-1", profileId: "uuid-123", role: 0);

        Assert.NotNull(token);
        Assert.Equal(1, token.OwnerUid);
        Assert.Equal("client-1", token.ClientToken);
        Assert.Equal("uuid-123", token.ProfileId);
        Assert.NotEmpty(token.AccessToken);
    }

    [Fact]
    public async Task ValidateAsync_ExistingToken_ShouldReturnToken()
    {
        var service = new InMemoryTokenService();
        var issued = await service.IssueAsync(1, "test@test.com", null, null, 0);

        var validated = await service.ValidateAsync(issued.AccessToken);

        Assert.NotNull(validated);
        Assert.Equal(issued.AccessToken, validated.AccessToken);
    }

    [Fact]
    public async Task ValidateAsync_NonExistentToken_ShouldReturnNull()
    {
        var service = new InMemoryTokenService();
        var result = await service.ValidateAsync("non-existent");
        Assert.Null(result);
    }

    [Fact]
    public async Task IssueAsync_ExistingEmail_ShouldRevokeOldToken()
    {
        var service = new InMemoryTokenService();
        var old = await service.IssueAsync(1, "test@test.com", null, null, 0);
        var newToken = await service.IssueAsync(1, "test@test.com", null, "new-uuid", 0);

        var oldValidated = await service.ValidateAsync(old.AccessToken);
        var newValidated = await service.ValidateAsync(newToken.AccessToken);

        Assert.Null(oldValidated);
        Assert.NotNull(newValidated);
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldRemoveToken()
    {
        var service = new InMemoryTokenService();
        var token = await service.IssueAsync(1, "test@test.com", null, null, 0);

        await service.RevokeAllAsync("test@test.com");

        var result = await service.ValidateAsync(token.AccessToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeByTokenAsync_ShouldRemoveToken()
    {
        var service = new InMemoryTokenService();
        var token = await service.IssueAsync(1, "test@test.com", null, null, 0);

        await service.RevokeByTokenAsync(token.AccessToken);

        var result = await service.ValidateAsync(token.AccessToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ShouldIssueNewToken()
    {
        var service = new InMemoryTokenService();
        var old = await service.IssueAsync(1, "test@test.com", "client-1", "old-uuid", 0);

        var newToken = await service.RefreshAsync(old.AccessToken, "client-1", "new-uuid");

        Assert.NotNull(newToken);
        Assert.NotEqual(old.AccessToken, newToken.AccessToken);
        Assert.Equal("new-uuid", newToken.ProfileId);
        Assert.Equal("client-1", newToken.ClientToken);
    }

    [Fact]
    public async Task RefreshAsync_WrongClientToken_ShouldReturnNull()
    {
        var service = new InMemoryTokenService();
        var old = await service.IssueAsync(1, "test@test.com", "client-1", null, 0);

        var result = await service.RefreshAsync(old.AccessToken, "wrong-client", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_NonExistentToken_ShouldReturnNull()
    {
        var service = new InMemoryTokenService();
        var result = await service.RefreshAsync("non-existent", null, null);
        Assert.Null(result);
    }
}

public class UuidGeneratorTests
{
    [Fact]
    public void GenerateV3_SameUsername_ShouldReturnSameUuid()
    {
        var uuid1 = UuidGenerator.GenerateV3("Steve");
        var uuid2 = UuidGenerator.GenerateV3("Steve");

        Assert.Equal(uuid1, uuid2);
    }

    [Fact]
    public void GenerateV3_DifferentUsername_ShouldReturnDifferentUuid()
    {
        var uuid1 = UuidGenerator.GenerateV3("Steve");
        var uuid2 = UuidGenerator.GenerateV3("Alex");

        Assert.NotEqual(uuid1, uuid2);
    }

    [Fact]
    public void GenerateV3_ShouldReturn32HexChars()
    {
        var uuid = UuidGenerator.GenerateV3("TestPlayer");

        Assert.Equal(32, uuid.Length);
        Assert.Matches("^[0-9A-F]{32}$", uuid);
    }

    [Fact]
    public void GenerateV3_CaseInsensitive_ShouldReturnSameUuid()
    {
        var uuid1 = UuidGenerator.GenerateV3("Steve");
        var uuid2 = UuidGenerator.GenerateV3("steve");

        Assert.Equal(uuid1, uuid2);
    }

    [Fact]
    public void GenerateV4_ShouldReturn32HexChars()
    {
        var uuid = UuidGenerator.GenerateV4();

        Assert.Equal(32, uuid.Length);
        Assert.Matches("^[0-9A-F]{32}$", uuid);
    }

    [Fact]
    public void GenerateV4_DifferentCalls_ShouldReturnDifferentUuids()
    {
        var uuid1 = UuidGenerator.GenerateV4();
        var uuid2 = UuidGenerator.GenerateV4();

        Assert.NotEqual(uuid1, uuid2);
    }

    [Fact]
    public void Generate_WithAlgorithm_ShouldDelegateCorrectly()
    {
        var uuidV3 = UuidGenerator.Generate("v3", "Steve");
        var uuidV4 = UuidGenerator.Generate("v4");

        Assert.Equal(32, uuidV3.Length);
        Assert.Equal(32, uuidV4.Length);
    }

    [Fact]
    public void Generate_V3WithoutUsername_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => UuidGenerator.Generate("v3"));
    }

    [Fact]
    public void Generate_UnsupportedAlgorithm_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => UuidGenerator.Generate("v5"));
    }
}
