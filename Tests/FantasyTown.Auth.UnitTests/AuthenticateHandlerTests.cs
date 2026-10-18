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
        await _lockoutService.LockAsync(user.Uid, TimeSpan.FromMinutes(15));

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
        await _lockoutService.RecordFailureAsync(user.Uid, user.Email, "127.0.0.1");

        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "password123"
        };

        var result = await _handler.HandleAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.False(await _lockoutService.IsLockedOutAsync(user.Uid));
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

        var isLocked = await _lockoutService.IsLockedOutAsync(user.Uid);
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

        // 同一 IP 连续失败 5 次（FAIL_EP:{email}:{ip} 窗口内 ≥5 → LOCKOUT:{uid}）
        for (int i = 0; i < 5; i++)
        {
            await _handler.HandleAsync(request, "127.0.0.1");
        }

        var isLocked = await _lockoutService.IsLockedOutAsync(user.Uid);
        Assert.True(isLocked);
    }

    [Fact]
    public async Task HandleAsync_CrossIpDistributedFailures_ShouldNotTriggerLockout()
    {
        // T3-003 回归：攻击者用 5 个不同 IP 各失败 1 次，不得锁死受害者账户
        // （仅同 IP 复合键 FAIL_EP:{email}:{ip} 达阈值才触发 LOCKOUT:{uid}）
        var user = await CreateUser();
        var request = new AuthenticateRequest
        {
            Username = "test@test.com",
            Password = "wrongpassword"
        };

        for (int i = 0; i < 5; i++)
        {
            var result = await _handler.HandleAsync(request, $"10.33.{i}.1");
            Assert.False(result.IsSuccess);
        }

        Assert.False(await _lockoutService.IsLockedOutAsync(user.Uid));
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
    private readonly Dictionary<string, List<long>> _windows = new();
    private readonly Dictionary<int, (int Count, DateTime ExpiresAt)> _lockouts = new();
    private readonly int _maxAttempts = 5;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);

    public Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;

        var epCount = AddWindowed($"FAIL_EP:{email}:{ip}", now, windowStart);
        AddWindowed($"FAIL_ACCT:{email}", now, windowStart);

        var triggered = epCount >= _maxAttempts;
        if (triggered)
        {
            var nextCount = _lockouts.TryGetValue(uid, out var prev) ? prev.Count + 1 : 1;
            _lockouts[uid] = (nextCount, DateTime.UtcNow.Add(_lockoutDuration));
        }

        return Task.FromResult(new LockoutFailureResult(epCount, triggered));
    }

    public Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)_window.TotalMilliseconds;
        AddWindowed($"FAIL_ACCT:{identifier}", now, windowStart);
        return Task.CompletedTask;
    }

    public Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(uid, out var state) && state.ExpiresAt > DateTime.UtcNow)
        {
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default)
    {
        if (_lockouts.TryGetValue(uid, out var state) && state.ExpiresAt > DateTime.UtcNow)
        {
            return Task.FromResult<double?>((state.ExpiresAt - DateTime.UtcNow).TotalSeconds);
        }
        return Task.FromResult<double?>(null);
    }

    public Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
    {
        _lockouts.Remove(uid);
        _windows.Remove($"FAIL_EP:{email}:{ip}");
        return Task.CompletedTask;
    }

    public Task LockAsync(int uid, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        _lockouts[uid] = (5, DateTime.UtcNow.Add(duration));
        return Task.CompletedTask;
    }

    private int AddWindowed(string key, long now, long windowStart)
    {
        if (!_windows.TryGetValue(key, out var list))
        {
            list = new List<long>();
            _windows[key] = list;
        }
        list.RemoveAll(t => t < windowStart);
        list.Add(now);
        return list.Count;
    }
}
