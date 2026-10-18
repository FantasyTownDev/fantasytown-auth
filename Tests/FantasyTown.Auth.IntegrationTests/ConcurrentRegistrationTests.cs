using System.Collections.Concurrent;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
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
    private bool _useTestcontainers;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        if (DockerHelper.IsAvailable())
        {
            _useTestcontainers = true;
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
            _useTestcontainers = false;
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
                var handler = new RegisterUserHandler(context, new FakePasswordService());
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
                var handler = new RegisterUserHandler(context, new FakePasswordService());
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
}

internal class FakePasswordService : IPasswordService
{
    public string HashPassword(string password) => "fakehash";
    public bool VerifyPassword(string password, string passwordHash) => true;
    public Task<bool> VerifyPasswordAsync(string password, string passwordHash, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public bool NeedsRehash(string passwordHash) => false;
}
