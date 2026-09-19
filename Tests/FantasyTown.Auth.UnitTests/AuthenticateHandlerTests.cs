using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class AuthenticateHandlerTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly InMemoryPasswordService _passwordService;
    private readonly InMemoryPermissionSnapshot _permissionSnapshot;
    private readonly InMemoryLockoutService _lockoutService;
    private readonly InMemoryTokenService _tokenService;
    private readonly AuthenticateHandler _handler;

    public AuthenticateHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        _passwordService = new InMemoryPasswordService();
        _permissionSnapshot = new InMemoryPermissionSnapshot();
        _lockoutService = new InMemoryLockoutService();
        _tokenService = new InMemoryTokenService();

        var yggOptions = new YggOptions();
        var concurrencyLimiter = new AuthConcurrencyLimiter();
        _handler = new AuthenticateHandler(_db, _passwordService, _permissionSnapshot, _lockoutService, _tokenService, yggOptions, concurrencyLimiter);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private async Task<User> CreateUser(string email = "test@test.com", string password = "password123", string? playerName = "TestPlayer")
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

        if (playerName != null)
        {
            var player = new Player
            {
                Uid = user.Uid,
                Name = playerName,
                Uuid = UuidGenerator.GenerateV3(playerName),
                IsBanned = false,
                LastModified = DateTime.UtcNow
            };
            _db.Players.Add(player);
            await _db.SaveChangesAsync();
        }

        return user;
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ShouldReturnSuccess()
    {
        var user = await CreateUser();
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123",
            ClientToken = "client-token-1"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.NotEmpty(result.Response.AccessToken);
        Assert.Equal("client-token-1", result.Response.ClientToken);
        Assert.Single(result.Response.AvailableProfiles);
        Assert.NotNull(result.Response.SelectedProfile);
    }

    [Fact]
    public async Task HandleAsync_InvalidPassword_ShouldReturnForbidden()
    {
        var user = await CreateUser();
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "wrongpassword"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Equal("ForbiddenOperationException", result.Error.Error);
    }

    [Fact]
    public async Task HandleAsync_NonExistentUser_ShouldReturnForbidden()
    {
        var request = new AuthenticateRequest
        {
            Username = "nonexistent@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_BannedUser_ShouldSkipPasswordCheck()
    {
        var user = await CreateUser();
        await _permissionSnapshot.SetAsync(user.Uid, new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        // 密码不应该被验证
        Assert.Equal(0, _passwordService.VerifyCount);
    }

    [Fact]
    public async Task HandleAsync_LockedUser_ShouldReturnForbidden()
    {
        var user = await CreateUser();
        await _lockoutService.LockAsync($"USER:{user.Uid}", TimeSpan.FromMinutes(15));

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedUser_ShouldReturnForbidden()
    {
        var user = await CreateUser();
        user.IsDeleted = true;
        await _db.SaveChangesAsync();

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulLogin_ShouldResetLockout()
    {
        var user = await CreateUser();
        // 模拟之前的失败尝试
        await _lockoutService.RecordFailureAsync($"USER:{user.Uid}");

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.False(await _lockoutService.IsLockedOutAsync($"USER:{user.Uid}"));
    }

    [Fact]
    public async Task HandleAsync_FailedLogin_ShouldRecordFailure()
    {
        var user = await CreateUser();
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "wrongpassword"
        };

        await _handler.HandleAsync(request, "127.0.0.1");

        var isLocked = await _lockoutService.IsLockedOutAsync($"USER:{user.Uid}");
        Assert.False(isLocked); // 第一次失败不会锁定
    }

    [Fact]
    public async Task HandleAsync_MultipleFailedLogins_ShouldTriggerLockout()
    {
        var user = await CreateUser();
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "wrongpassword"
        };

        // 连续失败 5 次
        for (int i = 0; i < 5; i++)
        {
            await _handler.HandleAsync(request, "127.0.0.1");
        }

        var isLocked = await _lockoutService.IsLockedOutAsync($"USER:{user.Uid}");
        Assert.True(isLocked);
    }

    [Fact]
    public async Task HandleAsync_NoPlayer_ShouldStillWork()
    {
        var user = await CreateUser(playerName: null);
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Empty(result.Response.AvailableProfiles);
        Assert.Null(result.Response.SelectedProfile);
    }

    [Fact]
    public async Task HandleAsync_PlayerWithoutUuid_ShouldGenerateUuid()
    {
        var user = await CreateUser();
        var player = await _db.Players.FirstAsync(p => p.Uid == user.Uid);
        player.Uuid = "";
        await _db.SaveChangesAsync();

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        var updatedPlayer = await _db.Players.FirstAsync(p => p.Uid == user.Uid);
        Assert.NotEmpty(updatedPlayer.Uuid);
        Assert.Equal(32, updatedPlayer.Uuid.Length);
    }
}

/// <summary>
/// InMemory 密码服务 - 记录验证次数
/// </summary>
public class InMemoryPasswordService : IPasswordService
{
    public int VerifyCount { get; private set; }

    public string HashPassword(string password)
    {
        return $"hashed_{password}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        VerifyCount++;
        return passwordHash == $"hashed_{password}";
    }

    public Task<bool> VerifyPasswordAsync(string password, string passwordHash, CancellationToken cancellationToken = default)
    {
        VerifyCount++;
        return Task.FromResult(passwordHash == $"hashed_{password}");
    }

    public bool NeedsRehash(string passwordHash)
    {
        return false;
    }
}

/// <summary>
/// InMemory 锁定服务
/// </summary>
public class InMemoryLockoutService : ILockoutService
{
    private readonly Dictionary<string, (int count, DateTime? lockedUntil)> _lockouts = new();

    public Task<int> RecordFailureAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_lockouts.ContainsKey(key))
            _lockouts[key] = (0, null);

        var (count, _) = _lockouts[key];
        count++;
        _lockouts[key] = (count, null);

        // 5 次失败后锁定 15 分钟
        if (count >= 5)
        {
            _lockouts[key] = (count, DateTime.UtcNow.AddMinutes(15));
        }

        return Task.FromResult(count);
    }

    public Task<bool> IsLockedOutAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(key, out var state) && state.lockedUntil.HasValue)
        {
            return Task.FromResult(state.lockedUntil.Value > DateTime.UtcNow);
        }
        return Task.FromResult(false);
    }

    public Task<double?> GetRemainingLockoutSecondsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(key, out var state) && state.lockedUntil.HasValue)
        {
            var remaining = (state.lockedUntil.Value - DateTime.UtcNow).TotalSeconds;
            return Task.FromResult<double?>(remaining > 0 ? remaining : 0);
        }
        return Task.FromResult<double?>(null);
    }

    public Task ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        _lockouts.Remove(key);
        return Task.CompletedTask;
    }

    public Task LockAsync(string key, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        _lockouts[key] = (5, DateTime.UtcNow.Add(duration));
        return Task.CompletedTask;
    }
}
