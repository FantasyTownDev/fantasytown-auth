using FantasyTown.Auth.Modules.Accounts.Infrastructure;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// T3-006：InMemory 锁定服务指数退避语义
/// （Redis 实现的 TTL 升级由 RedisAcceptanceTests 断言；此处覆盖两份内存镜像防漂移）
/// </summary>
public class InMemoryLockoutBackoffTests
{
    [Fact]
    public async Task ProdInMemory_RecordFailure_EscalatesTtl15To30To60()
    {
        var svc = new FantasyTown.Auth.Modules.Accounts.Infrastructure.InMemoryLockoutService(
            maxAttempts: 5, windowMinutes: 15, lockoutMinutes: 15);

        await AssertBackoffAsync(svc, uid: 1, $"a_{Guid.NewGuid():N}@t.local", "10.0.0.1");
    }

    [Fact]
    public async Task TestDouble_RecordFailure_EscalatesTtl15To30To60()
    {
        // 测试命名空间内的替身类（AuthenticateHandlerTests.cs 定义），语义须与生产实现一致
        var svc = new InMemoryLockoutService();

        await AssertBackoffAsync(svc, uid: 2, $"b_{Guid.NewGuid():N}@t.local", "10.0.0.2");
    }

    private static async Task AssertBackoffAsync(ILockoutService svc, int uid, string email, string ip)
    {
        // 阈值第 1 次触发 → 15min
        for (int i = 0; i < 5; i++)
        {
            await svc.RecordFailureAsync(uid, email, ip);
        }
        Assert.InRange((await svc.GetRemainingLockoutSecondsAsync(uid))!.Value, 850, 900);

        // 第 2 次触发 → 30min
        await svc.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await svc.GetRemainingLockoutSecondsAsync(uid))!.Value, 1750, 1800);

        // 第 3 次触发 → 60min
        await svc.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await svc.GetRemainingLockoutSecondsAsync(uid))!.Value, 3550, 3600);

        // 第 4 次触发 → 仍封顶 60min（不无限退避，§6.6 DoS 权衡）
        await svc.RecordFailureAsync(uid, email, ip);
        Assert.InRange((await svc.GetRemainingLockoutSecondsAsync(uid))!.Value, 3550, 3600);
    }
}

public class InMemoryLockoutClearTests
{
    /// <summary>
    /// V5 §7.3 管理员清除锁定：仅删 LOCKOUT 键并返回原状态，失败计数窗保留
    /// </summary>
    [Fact]
    public async Task ClearAsync_RemovesLockAndReportsPreviousState()
    {
        var svc = new InMemoryLockoutService();
        var uid = 7001;
        var email = "clear@test.com";
        var ip = "127.0.0.1";

        // 未锁定：清除返回 false
        Assert.False(await svc.ClearAsync(uid));

        for (int i = 0; i < 5; i++)
        {
            await svc.RecordFailureAsync(uid, email, ip);
        }
        Assert.True(await svc.IsLockedOutAsync(uid));

        // 已锁定：清除返回 true 且解锁
        Assert.True(await svc.ClearAsync(uid));
        Assert.False(await svc.IsLockedOutAsync(uid));

        // 失败计数窗未被触碰（再犯提醒语境）：再 1 次失败即重新达阈值
        var result = await svc.RecordFailureAsync(uid, email, ip);
        Assert.Equal(6, result.FailureCount);
    }
}
