using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 并发封禁测试（需要 MariaDB 或 Docker）
/// </summary>
[Trait("Category", "Integration")]
public class ConcurrentBanTests : IAsyncLifetime
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
    public async Task ConcurrentBan_ShouldHaveAtMostOneActiveBan()
    {
        const int concurrentCount = 20;
        using var context = CreateContext();

        var user = new User
        {
            Email = "test@example.com",
            Password = "hashedpassword",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var player = new Player
        {
            Uid = user.Uid,
            Name = "TestPlayer",
            Uuid = Guid.NewGuid().ToString("N").ToUpper()
        };
        context.Players.Add(player);
        await context.SaveChangesAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var ban = new PlayerBan
                {
                    Pid = player.Pid,
                    BannedAt = DateTime.UtcNow,
                    BannedBy = user.Uid,
                    BannedReason = "Test ban",
                    BannedUntil = DateTime.UtcNow.AddDays(1),
                    IsActive = true
                };
                ctx.PlayerBans.Add(ban);
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch
                {
                    // Expected for concurrent conflicts
                }
            }));
        }

        await Task.WhenAll(tasks);

        using var verifyContext = CreateContext();
        var activeBans = await verifyContext.PlayerBans
            .Where(b => b.Pid == player.Pid && b.IsActive && b.BannedUntil > DateTime.UtcNow)
            .CountAsync();

        Assert.True(activeBans <= 1, $"Expected at most 1 active ban, got {activeBans}");
    }
}
