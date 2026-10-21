using System.Collections.Concurrent;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 并发注册测试（需要 MariaDB 或 Docker）
/// 本地开发：配置 appsettings.IntegrationTests.json 指向本地 MariaDB
/// CI（有 Docker）：自动使用 Testcontainers 创建 MySQL 容器
/// </summary>
[Trait("Category", "Integration")]
public class ConcurrentRegistrationTests : IAsyncLifetime
{
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        if (DockerHelper.IsAvailable())
        {
            var container = new Testcontainers.MySql.MySqlBuilder()
                .WithDatabase("fantasytown_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await container.StartAsync();
            _connectionString = container.GetConnectionString();
        }
        else
        {
            _connectionString = TestDbContextFactory.GetConnectionString();
        }

        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
    }

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString))
            .Options;
        return new AuthDbContext(options);
    }

    [Fact]
    public async Task ConcurrentRegistration_ShouldAllowExactlyOneSuccess()
    {
        const int concurrentCount = 50;
        // 每次运行唯一邮箱/角色名：避免跨运行残留 / 跨测试类并行冲突（CI 与本地持久库通用）
        var rid = Guid.NewGuid().ToString("N")[..8];
        var runEmail = $"loadtest-{rid}@example.com";
        var runName = $"LoadPlyr{rid}";
        var results = new ConcurrentBag<RegisterUserResult>();
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var context = CreateContext();
                var handler = new RegisterUserHandler(context, new FakePasswordService(), new InMemoryRegistrationQuotaService(), new SiteOptions(), new MySqlAdvisoryOwnerClaimLock(context));
                var command = new RegisterUserCommand
                {
                    Email = runEmail,
                    Username = runName,
                    Password = "Password123!",
                    ClientIp = "127.0.0.1"
                };

                var result = await handler.HandleAsync(command);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        // 真实 MariaDB 只有1个成功（唯一约束）
        Assert.Equal(1, successCount);
        Assert.Equal(concurrentCount - 1, failureCount);
    }

    [Fact]
    public async Task ConcurrentRegistration_DifferentEmails_ShouldAllSucceed()
    {
        const int concurrentCount = 10;
        // 每次运行唯一前缀：邮箱与角色名均不与历史残留/其他测试类冲突
        var rid = Guid.NewGuid().ToString("N")[..8];
        var results = new ConcurrentBag<RegisterUserResult>();
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                using var context = CreateContext();
                var handler = new RegisterUserHandler(context, new FakePasswordService(), new InMemoryRegistrationQuotaService(), new SiteOptions(), new MySqlAdvisoryOwnerClaimLock(context));
                var command = new RegisterUserCommand
                {
                    Email = $"user{index}-{rid}@example.com",
                    Username = $"Player{index}_{rid}",
                    Password = "Password123!",
                    ClientIp = "127.0.0.1"
                };

                var result = await handler.HandleAsync(command);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.IsSuccess);
        Assert.Equal(concurrentCount, successCount);
    }

    /// <summary>
    /// M5/§4.1：唯一约束失败的注册请求不消耗 REG_IP 配额（防 NAT 出口 IP 配额 DoS）——
    /// 配额 3 = 成功 A + C + D；若失败的 B 计数，D 将被粗检拒绝
    /// </summary>
    [Fact]
    public async Task FailedRegistration_ShouldNotConsumeIpQuota()
    {
        const string ip = "172.16.0.50";
        var quota = new InMemoryRegistrationQuotaService(limit: 3);

        async Task<RegisterUserResult> RegisterAsync(string username, string email)
        {
            using var context = CreateContext();
            var handler = new RegisterUserHandler(context, new FakePasswordService(), quota, new SiteOptions(), new MySqlAdvisoryOwnerClaimLock(context));
            return await handler.HandleAsync(new RegisterUserCommand
            {
                Username = username,
                Email = email,
                Password = "Password123!",
                ClientIp = ip
            });
        }

        var unique = Guid.NewGuid().ToString("N")[..8];

        var a = await RegisterAsync($"qok_{unique}", $"qok_{unique}@example.com");
        Assert.True(a.IsSuccess);

        // 同邮箱冲突 → 落库失败（1062），必须发生在粗检之后、计数之前
        var b = await RegisterAsync($"qdup_{unique}", $"qok_{unique}@example.com");
        Assert.False(b.IsSuccess);
        Assert.Equal("邮箱已被注册", b.ErrorMessage);

        var c = await RegisterAsync($"qc_{unique}", $"qc_{unique}@example.com");
        Assert.True(c.IsSuccess);

        // 关键断言：B 未占用配额，第 3 个成功注册仍被放行
        var d = await RegisterAsync($"qd_{unique}", $"qd_{unique}@example.com");
        Assert.True(d.IsSuccess);
    }

    /// <summary>
    /// M4/T3-013：User+Player 单次保存原子落库——用户名唯一约束冲突时
    /// 不得留下孤儿 User（否则邮箱被无主占用、不可自愈）
    /// </summary>
    [Fact]
    public async Task FailedRegistration_DuplicateUsername_ShouldNotLeaveOrphanUser()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var conflictEmail = $"orphan_{unique}@example.com";

        using (var seedContext = CreateContext())
        {
            var seedHandler = new RegisterUserHandler(seedContext, new FakePasswordService(), new InMemoryRegistrationQuotaService(), new SiteOptions(), new MySqlAdvisoryOwnerClaimLock(seedContext));
            var seed = await seedHandler.HandleAsync(new RegisterUserCommand
            {
                Username = $"dup_{unique}",
                Email = $"dup_{unique}@example.com",
                Password = "Password123!",
                ClientIp = "127.0.0.1"
            });
            Assert.True(seed.IsSuccess);
        }

        // 同名不同邮箱 → 1062：User+Player 必须同一事务整体回滚
        using (var conflictContext = CreateContext())
        {
            var handler = new RegisterUserHandler(conflictContext, new FakePasswordService(), new InMemoryRegistrationQuotaService(), new SiteOptions(), new MySqlAdvisoryOwnerClaimLock(conflictContext));
            var result = await handler.HandleAsync(new RegisterUserCommand
            {
                Username = $"dup_{unique}",
                Email = conflictEmail,
                Password = "Password123!",
                ClientIp = "127.0.0.1"
            });
            Assert.False(result.IsSuccess);
            Assert.Equal("用户名已被占用", result.ErrorMessage);
        }

        using (var verifyContext = CreateContext())
        {
            var orphanExists = await verifyContext.Users.AnyAsync(u => u.Email == conflictEmail);
            Assert.False(orphanExists, "用户名冲突后不得留下孤儿 User（邮箱被无主占用）");
        }
    }

    /// <summary>
    /// 审计 C1 门禁（真实 MariaDB）：并发种子邮箱注册恰好产生一个 ServerOwner，
    /// 并落一条 actor=NULL 的 bootstrap=seed 审计。
    /// "仅生效一次"语义是库全局的：测试前后各清理一次 ServerOwner 行，保证可重复运行
    /// </summary>
    [Fact]
    public async Task ConcurrentSeedEmailRegistration_ExactlyOneOwner()
    {
        var rid = Guid.NewGuid().ToString("N")[..8];
        var seedEmail = $"seed-{rid}@example.com";
        var siteOptions = new SiteOptions { SeedOwnerEmail = seedEmail };
        const int concurrentCount = 8;

        // 前置清理：移除历史 ServerOwner（种子判定基于"库中无任何 ServerOwner"）
        using (var cleanup = CreateContext())
        {
            var legacyOwners = await cleanup.Users.IgnoreQueryFilters()
                .Where(u => u.Permission == UserPermission.ServerOwner)
                .ToListAsync();
            cleanup.Users.RemoveRange(legacyOwners);
            await cleanup.SaveChangesAsync();
        }

        var results = new ConcurrentBag<RegisterUserResult>();
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                using var context = CreateContext();
                var handler = new RegisterUserHandler(context, new FakePasswordService(), new InMemoryRegistrationQuotaService(), siteOptions, new MySqlAdvisoryOwnerClaimLock(context));
                results.Add(await handler.HandleAsync(new RegisterUserCommand
                {
                    Username = $"sd{index}_{rid}",
                    Email = seedEmail,
                    Password = "Password123!",
                    ClientIp = $"10.98.0.{index}"
                }));
            }));
        }

        await Task.WhenAll(tasks);

        // 同一种子邮箱：email UNIQUE 串行化落库，恰一个成功（且必须是被提权的那个）
        Assert.Equal(1, results.Count(r => r.IsSuccess));

        using var verify = CreateContext();
        var owners = await verify.Users.IgnoreQueryFilters()
            .Where(u => u.Permission == UserPermission.ServerOwner)
            .ToListAsync();
        Assert.Single(owners);
        Assert.Equal(seedEmail, owners[0].Email);

        var audit = await verify.AuthLogs.FirstOrDefaultAsync(l =>
            l.Action == "permission-change" && l.Context == "bootstrap=seed" && l.SubjectId == owners[0].Uid);
        Assert.NotNull(audit);
        Assert.Null(audit.ActorId);

        // 后置清理：移除本次产生的服主与审计，恢复库状态
        verify.Users.Remove(owners[0]);
        if (audit != null)
        {
            verify.AuthLogs.Remove(audit);
        }
        await verify.SaveChangesAsync();
    }
}

internal class FakePasswordService : IPasswordService
{
    public string HashPassword(string password) => "fakehash";
    public bool VerifyPassword(string password, string passwordHash) => true;
    public Task<bool> VerifyPasswordAsync(string password, string passwordHash, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public bool NeedsRehash(string passwordHash) => false;
}
