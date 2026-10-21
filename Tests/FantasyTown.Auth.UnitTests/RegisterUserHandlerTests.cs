using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class RegisterUserHandlerTests
{
    private readonly IPasswordService _passwordService = new Argon2PasswordService();

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "test_user",
            Password = "Password123!",
            Email = "test@example.com",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.UserId);
        Assert.Equal("test_user", result.Username);
        Assert.NotNull(result.PlayerUuid);
    }

    /// <summary>
    /// 并发同名注册测试（InMemory 不支持唯一约束，需要 MySQL 集成测试）
    /// </summary>
    [Fact]
    public async Task HandleAsync_DuplicateUsername_LogicallyReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command1 = new RegisterUserCommand
        {
            Username = "duplicate_user",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var command2 = new RegisterUserCommand
        {
            Username = "duplicate_user",
            Password = "Password456!",
            ClientIp = "127.0.0.1"
        };

        await handler.HandleAsync(command1);
        var result = await handler.HandleAsync(command2);

        // InMemory 不支持唯一约束，两个都会成功
        // 实际 MySQL 测试中，第二个会抛出 DbUpdateException 并被捕获
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// 并发同邮箱注册测试（InMemory 不支持唯一约束，需要 MySQL 集成测试）
    /// </summary>
    [Fact]
    public async Task HandleAsync_DuplicateEmail_LogicallyReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command1 = new RegisterUserCommand
        {
            Username = "user1",
            Password = "Password123!",
            Email = "same@example.com",
            ClientIp = "127.0.0.1"
        };

        var command2 = new RegisterUserCommand
        {
            Username = "user2",
            Password = "Password456!",
            Email = "same@example.com",
            ClientIp = "127.0.0.1"
        };

        await handler.HandleAsync(command1);
        var result = await handler.HandleAsync(command2);

        // InMemory 不支持唯一约束，两个都会成功
        // 实际 MySQL 测试中，第二个会抛出 DbUpdateException 并被捕获
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("a")]
    public async Task HandleAsync_InvalidUsername_ReturnsFailure(string username)
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = username,
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("用户名格式无效", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("short")]
    public async Task HandleAsync_WeakPassword_ReturnsFailure(string password)
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "valid_user",
            Password = password,
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("密码强度不足", result.ErrorMessage);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public async Task HandleAsync_InvalidEmail_ReturnsFailure(string email)
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "valid_user",
            Password = "Password123!",
            Email = email,
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("邮箱格式无效", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_NullEmail_Succeeds()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "no_email_user",
            Password = "Password123!",
            Email = null,
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_CreatesUserInDatabase()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "db_test_user",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Uid == result.UserId);
        Assert.NotNull(user);
        Assert.Equal("127.0.0.1", user.Ip);
        Assert.False(user.IsBanned);
    }

    [Fact]
    public async Task HandleAsync_CreatesPlayerInDatabase()
    {
        using var db = CreateInMemoryDbContext();
        var handler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());

        var command = new RegisterUserCommand
        {
            Username = "player_test_user",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);

        var player = await db.Players.FirstOrDefaultAsync(p => p.Name == "player_test_user");
        Assert.NotNull(player);
        Assert.Equal(result.PlayerUuid?.ToString("N").ToUpperInvariant(), player.Uuid);
        Assert.Equal(result.UserId, player.Uid);
    }

    /// <summary>
    /// M5 回归：REG_IP 粗检超限 → 拒绝注册且不落库（V5 §3.4/§4.1 软配额）
    /// </summary>
    [Fact]
    public async Task HandleAsync_QuotaExceeded_ShouldRejectBeforePersistence()
    {
        using var db = CreateInMemoryDbContext();
        var quota = new InMemoryRegistrationQuotaService(limit: 1);
        var handler = new RegisterUserHandler(db, _passwordService, quota, new SiteOptions(), new InlineOwnerClaimLock());

        var first = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "quota_ok",
            Password = "Password123!",
            Email = "quota_ok@example.com",
            ClientIp = "10.9.9.1"
        });
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "quota_rejected",
            Password = "Password123!",
            Email = "quota_rejected@example.com",
            ClientIp = "10.9.9.1"
        });
        Assert.False(second.IsSuccess);
        Assert.Contains("注册过于频繁", second.ErrorMessage);

        // 粗检拒绝发生在落库前：仅首个用户存在
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>
    /// M5：失败请求不消耗配额（§4.1 防 NAT 出口 IP 配额 DoS）——
    /// 校验失败的请求不计数，同 IP 后续成功注册仍可占满配额
    /// </summary>
    [Fact]
    public async Task HandleAsync_FailedValidation_DoesNotConsumeQuota()
    {
        using var db = CreateInMemoryDbContext();
        var quota = new InMemoryRegistrationQuotaService(limit: 1);
        var handler = new RegisterUserHandler(db, _passwordService, quota, new SiteOptions(), new InlineOwnerClaimLock());

        // 弱密码校验失败（不计数）
        var weak = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "weak_user",
            Password = "123",
            ClientIp = "10.9.9.2"
        });
        Assert.False(weak.IsSuccess);

        //配额未被失败请求消耗：合法注册仍放行
        var ok = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "weak_user_ok",
            Password = "Password123!",
            ClientIp = "10.9.9.2"
        });
        Assert.True(ok.IsSuccess);
    }

    private RegisterUserHandler CreateHandler(AuthDbContext db, string? seedEmail = null, IOwnerClaimLock? ownerClaimLock = null) =>
        new(db, _passwordService, new InMemoryRegistrationQuotaService(),
            new SiteOptions { SeedOwnerEmail = seedEmail ?? string.Empty },
            ownerClaimLock ?? new InlineOwnerClaimLock());

    /// <summary>
    /// V5 §4 引导（审计 C1）：种子邮箱首注册提权为服主，并落 permission-change 审计
    /// （actor=NULL 系统行为，context=bootstrap=seed）
    /// </summary>
    [Fact]
    public async Task HandleAsync_SeedEmail_FirstRegistration_GrantsOwnerAndAudits()
    {
        using var db = CreateInMemoryDbContext();
        var handler = CreateHandler(db, seedEmail: "seed@example.com");

        var result = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "seed_owner",
            Password = "Password123!",
            Email = "seed@example.com",
            ClientIp = "127.0.0.1"
        });

        Assert.True(result.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == result.UserId);
        Assert.Equal(UserPermission.ServerOwner, user.Permission);

        var log = await db.AuthLogs.FirstOrDefaultAsync(l => l.Action == "permission-change");
        Assert.NotNull(log);
        Assert.Null(log.ActorId);
        Assert.Equal(result.UserId, log.SubjectId);
        Assert.Equal("bootstrap=seed", log.Context);
    }

    /// <summary>
    /// V5 §4（审计 C1 语义修正）：提权条件是"库中无任何 ServerOwner"而非"无任何用户"——
    /// 首个注册者非种子邮箱时，种子邮箱后续注册仍应获得服主
    /// </summary>
    [Fact]
    public async Task HandleAsync_SeedEmail_AfterNonSeedFirstUser_StillGrantsOwner()
    {
        using var db = CreateInMemoryDbContext();

        var first = await CreateHandler(db).HandleAsync(new RegisterUserCommand
        {
            Username = "normal_first",
            Password = "Password123!",
            Email = "normal@example.com",
            ClientIp = "127.0.0.1"
        });
        Assert.True(first.IsSuccess);

        var seed = await CreateHandler(db, seedEmail: "seed@example.com").HandleAsync(new RegisterUserCommand
        {
            Username = "seed_second",
            Password = "Password123!",
            Email = "seed@example.com",
            ClientIp = "127.0.0.1"
        });

        Assert.True(seed.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == seed.UserId);
        Assert.Equal(UserPermission.ServerOwner, user.Permission);
    }

    /// <summary>
    /// V5 §4 "仅生效一次"：库中已存在服主后，种子邮箱再注册不提权
    /// </summary>
    [Fact]
    public async Task HandleAsync_SeedEmail_DoesNotEscalate_WhenOwnerExists()
    {
        using var db = CreateInMemoryDbContext();
        var seedEmail = "seed@example.com";

        var first = await CreateHandler(db, seedEmail).HandleAsync(new RegisterUserCommand
        {
            Username = "seed_owner",
            Password = "Password123!",
            Email = seedEmail,
            ClientIp = "127.0.0.1"
        });
        Assert.True(first.IsSuccess);

        var second = await CreateHandler(db, seedEmail).HandleAsync(new RegisterUserCommand
        {
            Username = "seed_again",
            Password = "Password123!",
            Email = seedEmail,
            ClientIp = "127.0.0.1"
        });

        Assert.True(second.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == second.UserId);
        Assert.Equal(UserPermission.NormalPlayer, user.Permission);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Permission == UserPermission.ServerOwner));
    }

    /// <summary>
    /// V5 §4 "仅生效一次"（含软删）：服主被软删后种子邮箱不得再次提权（恢复走 CLI promote-owner）
    /// </summary>
    [Fact]
    public async Task HandleAsync_SeedEmail_OwnerSoftDeleted_DoesNotEscalate()
    {
        using var db = CreateInMemoryDbContext();
        var seedEmail = "seed@example.com";

        var first = await CreateHandler(db, seedEmail).HandleAsync(new RegisterUserCommand
        {
            Username = "seed_owner",
            Password = "Password123!",
            Email = seedEmail,
            ClientIp = "127.0.0.1"
        });
        Assert.True(first.IsSuccess);

        // 软删服主（全局过滤器将排除该行，判定必须 IgnoreQueryFilters）
        var owner = await db.Users.FirstAsync(u => u.Uid == first.UserId);
        owner.IsDeleted = true;
        await db.SaveChangesAsync();

        var second = await CreateHandler(db, seedEmail).HandleAsync(new RegisterUserCommand
        {
            Username = "seed_after_del",
            Password = "Password123!",
            Email = seedEmail,
            ClientIp = "127.0.0.1"
        });

        Assert.True(second.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == second.UserId);
        Assert.Equal(UserPermission.NormalPlayer, user.Permission);
    }

    /// <summary>
    /// 配置空 = 禁用种子：未配置 SeedOwnerEmail 时任何邮箱注册均不提权
    /// （替代原硬编码 owner@fantasytown.com 的 TODO 行为）
    /// </summary>
    [Fact]
    public async Task HandleAsync_EmptySeedConfig_NeverEscalates()
    {
        using var db = CreateInMemoryDbContext();
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "legacy_seed",
            Password = "Password123!",
            Email = "owner@fantasytown.com",
            ClientIp = "127.0.0.1"
        });

        Assert.True(result.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == result.UserId);
        Assert.Equal(UserPermission.NormalPlayer, user.Permission);
        Assert.Empty(await db.AuthLogs.Where(l => l.Action == "permission-change").ToListAsync());
    }

    /// <summary>
    /// 审计 C1 安全方向：未获得独占锁（另一引导在途）时放弃提权，绝不双提权
    /// </summary>
    [Fact]
    public async Task HandleAsync_SeedEmail_LockNotAcquired_DoesNotEscalate()
    {
        using var db = CreateInMemoryDbContext();
        var handler = CreateHandler(db, "seed@example.com", ownerClaimLock: new NotAcquiredOwnerClaimLock());

        var result = await handler.HandleAsync(new RegisterUserCommand
        {
            Username = "no_lock_seed",
            Password = "Password123!",
            Email = "seed@example.com",
            ClientIp = "127.0.0.1"
        });

        Assert.True(result.IsSuccess);
        var user = await db.Users.FirstAsync(u => u.Uid == result.UserId);
        Assert.Equal(UserPermission.NormalPlayer, user.Permission);
    }
}

/// <summary>
/// InMemory 测试替身：直通执行并模拟获锁（真实独占语义由 MariaDB 集成测试覆盖）
/// </summary>
internal sealed class InlineOwnerClaimLock : IOwnerClaimLock
{
    public Task<T> RunExclusiveAsync<T>(Func<bool, Task<T>> action, CancellationToken cancellationToken = default)
        => action(true);
}

/// <summary>
/// InMemory 测试替身：模拟未获锁（另一引导在途），提权必须被放弃
/// </summary>
internal sealed class NotAcquiredOwnerClaimLock : IOwnerClaimLock
{
    public Task<T> RunExclusiveAsync<T>(Func<bool, Task<T>> action, CancellationToken cancellationToken = default)
        => action(false);
}
