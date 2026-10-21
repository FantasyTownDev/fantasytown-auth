using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// V5 §7.1 冻结管线顺序测试：越权矩阵先于★二次确认（防错 #12）、
/// 两失败分支同文案 403、确认失败计入 FAIL_EP 并审计 admin-denied、
/// 目标不存在 404、非敏感操作不验密。
/// </summary>
public class ManagementPipelineTests
{
    private const string OwnerPassword = "Secret1!";
    private const string OwnerPasswordHash = "hashed_Secret1!";

    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    private static User SeedOwner(AuthDbContext db, int uid = 1, string email = "owner@example.com") =>
        SeedUser(db, uid, email, UserPermission.ServerOwner);

    private static User SeedUser(AuthDbContext db, int uid, string email, UserPermission permission)
    {
        var user = new User
        {
            Uid = uid,
            Email = email,
            Password = permission == UserPermission.ServerOwner ? OwnerPasswordHash : $"hashed_{email}",
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static ManagementPipeline CreatePipeline(AuthDbContext db, InMemoryPasswordService passwords, RecordingLockoutService lockouts) =>
        new(db, passwords, lockouts, new StubClientIp());

    private sealed class StubClientIp : IClientIp
    {
        public string? GetClientIp() => "203.0.113.9";
    }

    private sealed class RecordingLockoutService : ILockoutService
    {
        public List<(int Uid, string Email, string Ip)> RecordedFailures { get; } = new();

        public Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
        {
            RecordedFailures.Add((uid, email, ip));
            return Task.FromResult(new LockoutFailureResult(1, false));
        }

        public Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default) =>
            Task.FromResult<double?>(null);

        public Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ClearAsync(int uid, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    [Fact]
    public async Task MatrixDenied_SkipsPasswordVerificationAndAudits()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "player@example.com", UserPermission.NormalPlayer);   // actor 无管理能力
        SeedOwner(db, uid: 2);                                                // 目标为服主
        var passwords = new InMemoryPasswordService();
        var lockouts = new RecordingLockoutService();
        var pipeline = CreatePipeline(db, passwords, lockouts);

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = 1,
            TargetUid = 2,
            Operation = "permission-change",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true,
            ActorCurrentPassword = OwnerPassword
        });

        Assert.False(result.IsAuthorized);
        Assert.Equal(403, result.StatusCode);
        // 防错 #12：注定 403 的越权请求不消耗验密 CPU
        Assert.Equal(0, passwords.VerifyCount);
        Assert.Empty(lockouts.RecordedFailures);
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "admin-denied" && l.ActorId == 1 && l.SubjectId == 2);
    }

    [Fact]
    public async Task ConfirmationFailure_MatchesMatrixDenialText_AndCountsFailEp()
    {
        using var db = CreateInMemoryDbContext();
        var owner = SeedOwner(db);
        SeedUser(db, 2, "target@example.com", UserPermission.NormalPlayer);
        var passwords = new InMemoryPasswordService();
        var lockouts = new RecordingLockoutService();
        var pipeline = CreatePipeline(db, passwords, lockouts);

        var confirmationFailure = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = owner.Uid,
            TargetUid = 2,
            Operation = "permission-change",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true,
            ActorCurrentPassword = "wrong-password"
        });

        Assert.False(confirmationFailure.IsAuthorized);
        Assert.Equal(403, confirmationFailure.StatusCode);
        Assert.Equal(ManagementPipeline.DenialMessage, confirmationFailure.DenialReason);
        // 确认失败计入 actor 的 FAIL_EP（uid/email/可信客户端 IP）
        Assert.Equal((owner.Uid, owner.Email, "203.0.113.9"), Assert.Single(lockouts.RecordedFailures));
        Assert.Contains(db.AuthLogs.Local, l =>
            l.Action == "admin-denied" && l.ActorId == owner.Uid && l.SubjectId == 2
            && l.Context == "permission-change:confirmation");

        // 两失败分支同文案 403（§7.1 原则 2：无信息泄露）
        var matrixFailure = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = owner.Uid,
            TargetUid = owner.Uid,   // 自我任命协管：NotSelf 拒绝
            Operation = "permission-change",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true,
            ActorCurrentPassword = OwnerPassword
        });

        Assert.Equal(403, matrixFailure.StatusCode);
        Assert.Equal(matrixFailure.DenialReason, confirmationFailure.DenialReason);
    }

    [Fact]
    public async Task ConfirmationSucceeds_AuthorizesWithoutSideEffects()
    {
        using var db = CreateInMemoryDbContext();
        var owner = SeedOwner(db);
        SeedUser(db, 2, "target@example.com", UserPermission.NormalPlayer);
        var passwords = new InMemoryPasswordService();
        var lockouts = new RecordingLockoutService();
        var pipeline = CreatePipeline(db, passwords, lockouts);

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = owner.Uid,
            TargetUid = 2,
            Operation = "permission-change",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true,
            ActorCurrentPassword = OwnerPassword
        });

        Assert.True(result.IsAuthorized);
        Assert.Equal(1, passwords.VerifyCount);
        Assert.Empty(lockouts.RecordedFailures);
        Assert.DoesNotContain(db.AuthLogs.Local, l => l.Action == "admin-denied");
    }

    [Fact]
    public async Task NonSensitiveOperation_AuthorizesWithoutVerification()
    {
        using var db = CreateInMemoryDbContext();
        var owner = SeedOwner(db);
        SeedUser(db, 2, "target@example.com", UserPermission.NormalPlayer);
        var passwords = new InMemoryPasswordService();
        var lockouts = new RecordingLockoutService();
        var pipeline = CreatePipeline(db, passwords, lockouts);

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = owner.Uid,
            TargetUid = 2,
            Operation = "ban",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = false
        });

        Assert.True(result.IsAuthorized);
        Assert.Equal(0, passwords.VerifyCount);
        Assert.Empty(lockouts.RecordedFailures);
    }

    [Fact]
    public async Task MissingPasswordOnSensitive_FailsConfirmation()
    {
        using var db = CreateInMemoryDbContext();
        SeedOwner(db);
        SeedUser(db, 2, "target@example.com", UserPermission.NormalPlayer);
        var passwords = new InMemoryPasswordService();
        var lockouts = new RecordingLockoutService();
        var pipeline = CreatePipeline(db, passwords, lockouts);

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = 1,
            TargetUid = 2,
            Operation = "delete",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true
            // 表单未携带 actor 当前密码
        });

        Assert.False(result.IsAuthorized);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(1, passwords.VerifyCount);
    }

    [Fact]
    public async Task TargetMissing_ReturnsNotFoundWithoutVerification()
    {
        using var db = CreateInMemoryDbContext();
        SeedOwner(db);
        var passwords = new InMemoryPasswordService();
        var pipeline = CreatePipeline(db, passwords, new RecordingLockoutService());

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = 1,
            TargetUid = 999,
            Operation = "force-reset-password",
            Matrix = ManagementMatrix.OwnerUser,
            Sensitive = true,
            ActorCurrentPassword = OwnerPassword
        });

        Assert.False(result.IsAuthorized);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("用户不存在", result.DenialReason);
        Assert.Equal(0, passwords.VerifyCount);
    }

    [Fact]
    public async Task ClearLockoutMatrix_SelfRescueAuthorizes()
    {
        // V5 评审 N1 回归锚点：唯一服主被锁的自救通道——自我解锁直接放行
        using var db = CreateInMemoryDbContext();
        var owner = SeedOwner(db);
        var pipeline = CreatePipeline(db, new InMemoryPasswordService(), new RecordingLockoutService());

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = owner.Uid,
            TargetUid = owner.Uid,
            Operation = "lockout-clear",
            Matrix = ManagementMatrix.ClearLockout,
            Sensitive = false
        });

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ClearLockoutMatrix_ModeratorCannotClearOwnerLockout()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "mod@example.com", UserPermission.ServerModerator);
        SeedOwner(db, uid: 2);
        var pipeline = CreatePipeline(db, new InMemoryPasswordService(), new RecordingLockoutService());

        var result = await pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = 1,
            TargetUid = 2,
            Operation = "lockout-clear",
            Matrix = ManagementMatrix.ClearLockout,
            Sensitive = false
        });

        Assert.False(result.IsAuthorized);
        Assert.Equal(403, result.StatusCode);
    }
}
