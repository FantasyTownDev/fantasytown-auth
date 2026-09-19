using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class ValidateHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryTokenService _tokenService;
    private readonly InMemoryPermissionSnapshot _permissionSnapshot;
    private readonly ValidateHandler _handler;

    public ValidateHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _tokenService = new InMemoryTokenService();
        _permissionSnapshot = new InMemoryPermissionSnapshot();

        var yggOptions = new YggOptions();
        _handler = new ValidateHandler(_tokenService, _permissionSnapshot, yggOptions);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_ValidToken_ShouldReturnValid()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", "client-1", "uuid-123", 0);

        var result = await _handler.HandleAsync(token.AccessToken, "client-1");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_NonExistentToken_ShouldReturnInvalid()
    {
        var result = await _handler.HandleAsync("non-existent", null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_WrongClientToken_ShouldReturnInvalid()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", "client-1", null, 0);

        var result = await _handler.HandleAsync(token.AccessToken, "wrong-client");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_NullClientToken_ShouldPass()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", "client-1", null, 0);

        var result = await _handler.HandleAsync(token.AccessToken, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_BannedUser_ShouldReturnInvalid()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", null, null, 0);
        await _permissionSnapshot.SetAsync(1, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });

        var result = await _handler.HandleAsync(token.AccessToken, null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_NoSnapshot_ShouldReturnValid()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", null, null, 0);

        // 无快照 = 允许（哨兵语义由中间件处理）
        var result = await _handler.HandleAsync(token.AccessToken, null);

        Assert.True(result.IsValid);
    }
}

public class InvalidateHandlerTests : IDisposable
{
    private readonly InMemoryTokenService _tokenService;
    private readonly InvalidateHandler _handler;

    public InvalidateHandlerTests()
    {
        _tokenService = new InMemoryTokenService();
        _handler = new InvalidateHandler(_tokenService);
    }

    public void Dispose() { }

    [Fact]
    public async Task HandleAsync_ExistingToken_ShouldRevoke()
    {
        var token = await _tokenService.IssueAsync(1, "test@test.com", null, null, 0);

        await _handler.HandleAsync(token.AccessToken);

        var validated = await _tokenService.ValidateAsync(token.AccessToken);
        Assert.Null(validated);
    }

    [Fact]
    public async Task HandleAsync_NonExistentToken_ShouldNotThrow()
    {
        await _handler.HandleAsync("non-existent");
    }
}

public class SignoutHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryPasswordService _passwordService;
    private readonly InMemoryTokenService _tokenService;
    private readonly SignoutHandler _handler;

    public SignoutHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _passwordService = new InMemoryPasswordService();
        _tokenService = new InMemoryTokenService();
        _handler = new SignoutHandler(_db, _passwordService, _tokenService);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private async Task CreateUser(string email = "test@test.com", string password = "password123")
    {
        var hash = _passwordService.HashPassword(password);
        var user = new User
        {
            Email = email,
            Password = hash,
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
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ShouldRevokeAllTokens()
    {
        await CreateUser();
        await _tokenService.IssueAsync(1, "test@test.com", null, null, 0);

        var result = await _handler.HandleAsync("test@test.com", "password123");

        Assert.True(result.IsValid);
        var validated = await _tokenService.ValidateAsync((await _tokenService.IssueAsync(1, "test@test.com", null, null, 0)).AccessToken);
        // 验证全吊销后旧令牌无效
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ShouldReturnInvalid()
    {
        await CreateUser();

        var result = await _handler.HandleAsync("test@test.com", "wrongpassword");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_NonExistentUser_ShouldReturnInvalid()
    {
        var result = await _handler.HandleAsync("nonexistent@test.com", "password123");
        Assert.False(result.IsValid);
    }
}

public class RefreshHandlerTests : IDisposable
{
    private readonly InMemoryTokenService _tokenService;
    private readonly InMemoryPermissionSnapshot _permissionSnapshot;
    private readonly RefreshHandler _handler;

    public RefreshHandlerTests()
    {
        _tokenService = new InMemoryTokenService();
        _permissionSnapshot = new InMemoryPermissionSnapshot();
        var yggOptions = new YggOptions();
        _handler = new RefreshHandler(_tokenService, _permissionSnapshot, yggOptions);
    }

    public void Dispose() { }

    [Fact]
    public async Task HandleAsync_ValidToken_ShouldReturnNewToken()
    {
        var old = await _tokenService.IssueAsync(1, "test@test.com", "client-1", "old-uuid", 0);

        var result = await _handler.HandleAsync(old.AccessToken, "client-1", "new-uuid");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Response);
        Assert.NotEqual(old.AccessToken, result.Response.AccessToken);
    }

    [Fact]
    public async Task HandleAsync_NonExistentToken_ShouldReturnInvalid()
    {
        var result = await _handler.HandleAsync("non-existent", null, null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_WrongClientToken_ShouldReturnInvalid()
    {
        var old = await _tokenService.IssueAsync(1, "test@test.com", "client-1", null, 0);

        var result = await _handler.HandleAsync(old.AccessToken, "wrong-client", null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HandleAsync_BannedUser_ShouldReturnInvalid()
    {
        var old = await _tokenService.IssueAsync(1, "test@test.com", null, null, 0);
        await _permissionSnapshot.SetAsync(1, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });

        var result = await _handler.HandleAsync(old.AccessToken, null, null);

        Assert.False(result.IsValid);
    }
}
