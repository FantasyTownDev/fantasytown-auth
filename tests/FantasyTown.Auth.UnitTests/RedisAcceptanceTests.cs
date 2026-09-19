using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Application.Queries;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// P1 验收测试 - 使用真实 Redis
/// </summary>
public class RedisAcceptanceTests : IDisposable
{
    private readonly IPasswordService _passwordService = new Argon2PasswordService();
    private readonly IConnectionMultiplexer _redis;
    private readonly ILockoutService _lockoutService;

    public RedisAcceptanceTests()
    {
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        // 使用 DB 15 隔离测试数据，避免跨测试/跨运行污染
        _lockoutService = new RedisLockoutService(_redis, database: 15);
        // 测试前清理 DB 15 所有键
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: "*", database: 15);
        foreach (var key in keys)
        {
            _redis.GetDatabase(15).KeyDelete(key);
        }
    }

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    /// <summary>
    /// 同 IP 5 次失败 → LOCKOUT 生效
    /// </summary>
    [Fact]
    public async Task Lockout_AfterFiveFailedAttempts_ShouldBeLocked()
    {
        var identifier = $"IP:192.168.1.100_{Guid.NewGuid()}";

        // 记录 5 次失败
        for (int i = 0; i < 5; i++)
        {
            await _lockoutService.RecordFailureAsync(identifier);
        }

        // 应该被锁定
        var isLocked = await _lockoutService.IsLockedOutAsync(identifier);
        Assert.True(isLocked);

        // 应该有剩余锁定时间
        var remaining = await _lockoutService.GetRemainingLockoutSecondsAsync(identifier);
        Assert.NotNull(remaining);
        Assert.True(remaining > 0);

        // 清理
        await _lockoutService.ResetAsync(identifier);
    }

    /// <summary>
    /// 4 次失败 → 不锁定
    /// </summary>
    [Fact]
    public async Task Lockout_FourFailedAttempts_ShouldNotBeLocked()
    {
        var identifier = $"IP:192.168.1.101_{Guid.NewGuid()}";

        // 记录 4 次失败
        for (int i = 0; i < 4; i++)
        {
            await _lockoutService.RecordFailureAsync(identifier);
        }

        // 不应该被锁定
        var isLocked = await _lockoutService.IsLockedOutAsync(identifier);
        Assert.False(isLocked);

        // 清理
        await _lockoutService.ResetAsync(identifier);
    }

    /// <summary>
    /// 登录失败记录到 Redis
    /// </summary>
    [Fact]
    public async Task LoginFailed_ShouldRecordToRedis()
    {
        using var db = CreateInMemoryDbContext();
        var registerHandler = new RegisterUserHandler(db, _passwordService);
        var username = $"lok_{Guid.NewGuid().ToString("N")[..8]}";
        var clientIp = $"10.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";

        // 注册用户
        var regResult = await registerHandler.HandleAsync(new RegisterUserCommand
        {
            Username = username,
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        });
        Assert.True(regResult.IsSuccess, $"Registration failed: {regResult.ErrorMessage}");

        // 使用错误密码登录 5 次
        var permissionSnapshot = new InMemoryPermissionSnapshot();
        var loginHandler = new LoginHandler(db, _passwordService, permissionSnapshot, _lockoutService);

        LoginResult? lastLoginResult = null;
        try
        {
            for (int i = 0; i < 5; i++)
            {
                lastLoginResult = await loginHandler.HandleAsync(new LoginQuery
                {
                    Username = username,
                    Password = "WrongPassword!",
                    ClientIp = clientIp
                });
            }

            // IP 应该被锁定
            var isLocked = await _lockoutService.IsLockedOutAsync($"IP:{clientIp}");
            Assert.True(isLocked,
                $"IP:{clientIp} should be locked after 5 failed attempts. " +
                $"Last login result: IsSuccess={lastLoginResult?.IsSuccess}, Error={lastLoginResult?.ErrorMessage}");
        }
        finally
        {
            await _lockoutService.ResetAsync($"IP:{clientIp}");
        }
    }

    /// <summary>
    /// 权限快照设置和获取
    /// </summary>
    [Fact]
    public async Task PermissionSnapshot_SetAndGet_ShouldWork()
    {
        var snapshot = new RedisPermissionSnapshot(_redis, CreateInMemoryDbContext(), database: 15);
        var uid = 99999;

        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.ServerModerator,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(uid, permission);
        var result = await snapshot.GetAsync(uid);

        Assert.NotNull(result);
        Assert.Equal(UserPermission.ServerModerator, result.Permission);

        // 清理
        await snapshot.RemoveAsync(uid);
    }

    /// <summary>
    /// 权限快照移除
    /// </summary>
    [Fact]
    public async Task PermissionSnapshot_Remove_ShouldWork()
    {
        var snapshot = new RedisPermissionSnapshot(_redis, CreateInMemoryDbContext(), database: 15);
        var uid = 99998;

        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(uid, permission);
        await snapshot.RemoveAsync(uid);
        var result = await snapshot.GetAsync(uid);

        Assert.Null(result);
    }

    /// <summary>
    /// 协管权限撤销 → 快照清除
    /// </summary>
    [Fact]
    public async Task RevokePermission_ShouldClearSnapshot()
    {
        var snapshot = new RedisPermissionSnapshot(_redis, CreateInMemoryDbContext(), database: 15);
        var uid = 99997;

        // 设置协管权限快照
        var permission = new PermissionSnapshot
        {
            Permission = UserPermission.ServerModerator,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        await snapshot.SetAsync(uid, permission);

        // 撤销权限（清除快照）
        await snapshot.RemoveAsync(uid);

        // 快照应该为空
        var result = await snapshot.GetAsync(uid);
        Assert.Null(result);
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
