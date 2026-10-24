using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;
using InfraLockoutService = FantasyTown.Auth.Modules.Accounts.Infrastructure.InMemoryLockoutService;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// 锁定触发审计测试（V5 §6.6：锁定触发写 lockout 审计——受害账户/来源 IP/计数）。
/// 注意：单元测试工程内存在同名测试桩 InMemoryLockoutService（AuthenticateHandlerTests），
/// 此处经别名显式使用 Infrastructure 实现。
/// </summary>
public class LockoutAuditTests
{
    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    [Fact]
    public async Task BelowThreshold_WritesNoLockoutAudit()
    {
        using var db = CreateInMemoryDbContext();
        var svc = new InfraLockoutService(auditDb: db);

        for (var i = 0; i < 4; i++)
            await svc.RecordFailureAsync(7, "victim@example.com", "203.0.113.5");

        Assert.DoesNotContain(db.AuthLogs.Local, l => l.Action == "lockout");
    }

    [Fact]
    public async Task AtThreshold_WritesLockoutAuditWithVictimContext()
    {
        using var db = CreateInMemoryDbContext();
        var svc = new InfraLockoutService(auditDb: db);

        for (var i = 0; i < 5; i++)
            await svc.RecordFailureAsync(7, "victim@example.com", "203.0.113.5");

        var log = Assert.Single(db.AuthLogs.Local, l => l.Action == "lockout");
        Assert.Equal(7, log.SubjectId);
        Assert.Equal("203.0.113.5", log.Ip);
        Assert.Contains("count=5", log.Context);
        Assert.Contains("email=victim@example.com", log.Context);
    }

    [Fact]
    public async Task ReTriggerBeyondThreshold_AuditsEachOccurrence()
    {
        // 触发后每次阈值内失败都重新触发（INCR 续期），逐次落审计供定向攻击识别
        using var db = CreateInMemoryDbContext();
        var svc = new InfraLockoutService(auditDb: db);

        for (var i = 0; i < 7; i++)
            await svc.RecordFailureAsync(7, "victim@example.com", "203.0.113.5");

        Assert.Equal(3, db.AuthLogs.Local.Count(l => l.Action == "lockout"));
    }

    [Fact]
    public async Task WithoutAuditContext_StillFunctions()
    {
        // 可选依赖：未注入审计上下文时锁定语义不受影响（既有调用点兼容）
        var svc = new InfraLockoutService();

        for (var i = 0; i < 5; i++)
            await svc.RecordFailureAsync(1, "a@example.com", "198.51.100.1");

        Assert.True(await svc.IsLockedOutAsync(1));
    }
}
