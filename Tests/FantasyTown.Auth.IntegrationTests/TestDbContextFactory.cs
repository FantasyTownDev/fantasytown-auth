using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 测试数据库工厂，支持两种模式：
/// - CI（有 Docker）：使用 Testcontainers 自动创建 MySQL 容器
/// - 本地开发（无 Docker）：使用本地 MariaDB 服务
/// </summary>
public static class TestDbContextFactory
{
    public static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.IntegrationTests.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var ciConnectionString = configuration["IntegrationTests:ConnectionString"];
        if (!string.IsNullOrEmpty(ciConnectionString))
        {
            return ciConnectionString;
        }

        // 本地开发：使用本地 MariaDB
        return "Server=localhost;Port=3306;Database=fantasytown_integration_test;User=root;Password=;";
    }

    public static AuthDbContext CreateContext()
    {
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .Options;
        return new AuthDbContext(options);
    }

    public static async Task EnsureDatabaseAsync()
    {
        using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
