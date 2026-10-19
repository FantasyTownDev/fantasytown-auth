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
    /// V5 §6.6：同 IP 同账户 5 次失败 → 触发 LOCKOUT:{uid}（冻结键名直接断言）
    /// </summary>
    [Fact]
    public async Task Lockout_FiveSameIpFailures_ShouldTriggerLockout()
    {
        var uid = Random.Shared.Next(50000, 60000);
        var email = $"victim_{Guid.NewGuid():N}@test.local";
        var ip = "192.168.1.100";

        LockoutFailureResult? last = null;
        for (int i = 0; i < 5; i++)
        {
            last = await _lockoutService.RecordFailureAsync(uid, email, ip);
        }

        Assert.Equal(5, last!.FailureCount);
        Assert.True(last.LockoutTriggered);
        Assert.True(await _lockoutService.IsLockedOutAsync(uid));

        // §3.4 冻结键空间直接断言
        var db = _redis.GetDatabase(15);
        Assert.True(await db.KeyExistsAsync($"LOCKOUT:{uid}"));
        Assert.Equal(5, await db.SortedSetLengthAsync($"FAIL_EP:{email}:{ip}"));
        Assert.Equal(5, await db.SortedSetLengthAsync($"FAIL_ACCT:{email}"));

        // 应该有剩余锁定时间
        var remaining = await _lockoutService.GetRemainingLockoutSecondsAsync(uid);
        Assert.NotNull(remaining);
        Assert.True(remaining > 0);

        // 清理
        await _lockoutService.ResetAsync(uid, email, ip);
    }

    /// <summary>
    /// V5 §6.6：同 IP 4 次失败 → 不触发锁定
    /// </summary>
    [Fact]
    public async Task Lockout_FourSameIpFailures_ShouldNotTriggerLockout()
    {
        var uid = Random.Shared.Next(50000, 60000);
        var email = $"victim_{Guid.NewGuid():N}@test.local";
        var ip = "192.168.1.101";

        LockoutFailureResult? last = null;
        for (int i = 0; i < 4; i++)
        {
            last = await _lockoutService.RecordFailureAsync(uid, email, ip);
        }

        Assert.Equal(4, last!.FailureCount);
        Assert.False(last.LockoutTriggered);
        Assert.False(await _lockoutService.IsLockedOutAsync(uid));

        // 清理
        await _lockoutService.ResetAsync(uid, email, ip);
    }

    /// <summary>
    /// T3-003 回归：跨 IP 分散失败（5 个不同 IP 各 1 次）不得触发锁定，
    /// 仅计入 FAIL_ACCT:{email}（审计维度，不锁定）
    /// </summary>
    [Fact]
    public async Task Lockout_CrossIpDistributedFailures_ShouldNotTriggerLockout()
    {
        var uid = Random.Shared.Next(50000, 60000);
        var email = $"victim_{Guid.NewGuid():N}@test.local";

        for (int i = 0; i < 5; i++)
        {
            var result = await _lockoutService.RecordFailureAsync(uid, email, $"10.10.{i}.1");
            Assert.False(result.LockoutTriggered);
        }

        Assert.False(await _lockoutService.IsLockedOutAsync(uid));

        // 冻结键空间断言：无 LOCKOUT，FAIL_ACCT 累计 5（审计维度）
        var db = _redis.GetDatabase(15);
        Assert.False(await db.KeyExistsAsync($"LOCKOUT:{uid}"));
        Assert.Equal(5, await db.SortedSetLengthAsync($"FAIL_ACCT:{email}"));
    }

    /// <summary>
    /// H4 回归：并发失败下"检查+递增+锁定"单脚本原子执行——
    /// 5 次并发同 IP 失败必然触发锁定且计数零丢失（修复前读写分离会漏触发）
    /// </summary>
    [Fact]
    public async Task Lockout_ConcurrentSameIpFailures_ShouldTriggerWithoutLostCounts()
    {
        var uid = Random.Shared.Next(50000, 60000);
        var email = $"burst_{Guid.NewGuid():N}@test.local";
        var ip = "192.168.1.200";

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => _lockoutService.RecordFailureAsync(uid, email, ip)));

        Assert.Contains(results, r => r.LockoutTriggered);
        Assert.True(await _lockoutService.IsLockedOutAsync(uid));

        var db = _redis.GetDatabase(15);
        Assert.Equal(5, await db.SortedSetLengthAsync($"FAIL_EP:{email}:{ip}"));

        // 清理
        await _lockoutService.ResetAsync(uid, email, ip);
    }

    /// <summary>
    /// T3-006：LOCKOUT TTL 按触发次数指数退避 15→30→60min 封顶（V5 §6.6/§8）
    /// </summary>
    [Fact]
    public async Task Lockout_RepeatedTriggers_ShouldEscalateTtl15To30To60()
    {
        var uid = Random.Shared.Next(50000, 60000);
        var email = $"bo_{Guid.NewGuid():N}@test.local";
        var ip = "192.168.1.300";
        var db = _redis.GetDatabase(15);

        // 第 1 次触发 → 15min（容差：整秒 TTL + 查询耗时）
        for (int i = 0; i < 5; i++) await _lockoutService.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await db.KeyTimeToLiveAsync($"LOCKOUT:{uid}"))!.Value.TotalSeconds, 850, 900);

        // 锁定期内再次触发（直接调服务，模拟并发在途请求）→ 第 2 次触发 → 30min
        await _lockoutService.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await db.KeyTimeToLiveAsync($"LOCKOUT:{uid}"))!.Value.TotalSeconds, 1750, 1800);

        // 第 3 次触发 → 60min
        await _lockoutService.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await db.KeyTimeToLiveAsync($"LOCKOUT:{uid}"))!.Value.TotalSeconds, 3550, 3600);

        // 第 4 次触发 → 仍封顶 60min（不无限退避）
        await _lockoutService.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await db.KeyTimeToLiveAsync($"LOCKOUT:{uid}"))!.Value.TotalSeconds, 3550, 3600);

        // 清理
        await _lockoutService.ResetAsync(uid, email, ip);
    }

    /// <summary>
    /// M2 回归：FAIL_IP 固定窗计数单脚本原子执行——
    /// 首次请求即写入 TTL（修复前 INCR/EXPIRE 分离，中断则键永不过期，IP 被永久限死）
    /// </summary>
    [Fact]
    public async Task IpRateLimiter_FirstHit_SetsWindowTtlAtomically()
    {
        var limiter = new FantasyTown.Auth.Modules.Yggdrasil.Authserver.IpRateLimiter(_redis, limit: 3, database: 15);
        var key = $"m2_{Guid.NewGuid():N}";

        Assert.True(await limiter.IsAllowedAsync(key));

        var db = _redis.GetDatabase(15);
        var ttl = await db.KeyTimeToLiveAsync($"FAIL_IP:{key}");
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 30, 60);

        // 阈值语义保持：limit 内放行，超限拒绝
        Assert.True(await limiter.IsAllowedAsync(key));
        Assert.True(await limiter.IsAllowedAsync(key));
        Assert.False(await limiter.IsAllowedAsync(key));

        // 清理
        await db.KeyDeleteAsync($"FAIL_IP:{key}");
    }

    /// <summary>
    /// Web 登录同 IP 失败 5 次 → LOCKOUT:{uid} 生效
    /// （Ygg authenticate 检查同一键空间，任一通道触发即全域锁定，§6.6）
    /// </summary>
    [Fact]
    public async Task LoginFailed_ShouldLockAccountViaSharedKeyspace()
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
        var uid = regResult.UserId!.Value;

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

            // LOCKOUT:{uid} 键空间直接断言（Web 触发，Ygg 通道检查同一键）
            var db15 = _redis.GetDatabase(15);
            Assert.True(await db15.KeyExistsAsync($"LOCKOUT:{uid}"),
                $"LOCKOUT:{uid} should exist after 5 same-IP failures. " +
                $"Last login result: IsSuccess={lastLoginResult?.IsSuccess}, Error={lastLoginResult?.ErrorMessage}");
        }
        finally
        {
            var user = await db.Users.FirstAsync(u => u.Uid == uid);
            await _lockoutService.ResetAsync(uid, user.Email, clientIp);
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

        // C3 / V5 N2：移除后回源空结果返回封禁等效哨兵，不再是 null
        Assert.NotNull(result);
        Assert.True(result!.IsBanned);
        Assert.Null(result.BannedUntil);
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

        // C3 / V5 N2：撤销后回源空 = 封禁等效哨兵（Web 会话即时失效）
        var result = await snapshot.GetAsync(uid);
        Assert.NotNull(result);
        Assert.True(result!.IsBanned);
    }

    /// <summary>
    /// C3 / V5 N2：回源空结果 = 封禁等效哨兵且写入冻结键名缓存（防穿透）
    /// </summary>
    [Fact]
    public async Task PermissionSnapshot_EmptySource_ReturnsCachedBanSentinel()
    {
        var snapshot = new RedisPermissionSnapshot(_redis, CreateInMemoryDbContext(), database: 15);
        var uid = 99996;

        var result = await snapshot.GetAsync(uid);

        Assert.NotNull(result);
        Assert.True(result!.IsBanned);
        Assert.Null(result.BannedUntil);
        Assert.Equal(UserPermission.NormalPlayer, result.Permission);

        // 哨兵是可缓存值：写入 PERM:{uid}（冻结键名）防 miss-on-empty 回源风暴
        var db15 = _redis.GetDatabase(15);
        Assert.True(await db15.KeyExistsAsync($"PERM:{uid}"));

        // 清理
        await snapshot.RemoveAsync(uid);
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
