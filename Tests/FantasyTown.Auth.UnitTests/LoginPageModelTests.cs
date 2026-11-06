// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Application.Queries;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Pages.Account;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// H1 回归：LoginModel 必须把构造注入的 ILockoutService 传入 LoginHandler。
/// 此前页面手构 3 参 handler → 回落每请求新建的 InMemoryLockoutService →
/// 锁定计数当请求即弃 = Web 登录永不锁定（防错 #6：Web/Ygg 共享 LOCKOUT 键空间）。
/// </summary>
public class LoginPageModelTests
{
    private readonly IPasswordService _passwordService = new Argon2PasswordService();
    private readonly IPermissionSnapshot _permissionSnapshot = new InMemoryPermissionSnapshot();

    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    private async Task CreateTestUser(AuthDbContext db, string username = "testuser", string email = "test@example.com")
    {
        var registerHandler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());
        var result = await registerHandler.HandleAsync(new RegisterUserCommand
        {
            Username = username,
            Password = "Password123!",
            Email = email,
            ClientIp = "127.0.0.1"
        });

        Assert.True(result.IsSuccess);
    }

    private LoginModel CreatePageModel(AuthDbContext db, ILockoutService lockoutService)
    {
        return new LoginModel(db, _passwordService, _permissionSnapshot, new StubClientIp("203.0.113.7"), lockoutService);
    }

    [Fact]
    public async Task OnPost_WrongPassword_RecordsFailureOnInjectedLockoutService()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var lockout = new RecordingLockoutService();
        var model = CreatePageModel(db, lockout);
        model.Input = new LoginModel.InputModel { Username = "test@example.com", Password = "WrongPassword1!" };

        var result = await model.OnPostAsync();

        Assert.NotNull(result); // 返回 Page()，非重定向
        Assert.Equal(1, lockout.RecordFailureCalls);
        Assert.Equal("用户名或密码错误", model.ErrorMessage);
    }

    [Fact]
    public async Task OnPost_LockedAccount_RejectedViaInjectedService_BeforePasswordCheck()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var lockout = new RecordingLockoutService { LockedOut = true };
        var model = CreatePageModel(db, lockout);
        // 密码本身是正确的——若仍被拒，证明锁定判定走了注入实例而非本地空服务
        model.Input = new LoginModel.InputModel { Username = "test@example.com", Password = "Password123!" };

        var result = await model.OnPostAsync();

        Assert.NotNull(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Equal(0, lockout.RecordFailureCalls); // 锁定检查前置，未进入验密/记失败
    }

    [Fact]
    public async Task OnPost_UnknownAccount_RecordsUnknownFailureOnInjectedService()
    {
        using var db = CreateInMemoryDbContext();

        var lockout = new RecordingLockoutService();
        var model = CreatePageModel(db, lockout);
        model.Input = new LoginModel.InputModel { Username = "ghost@example.com", Password = "Whatever123!" };

        var result = await model.OnPostAsync();

        Assert.NotNull(result);
        Assert.Equal(1, lockout.UnknownAccountCalls);
    }

    private sealed class RecordingLockoutService : ILockoutService
    {
        public bool LockedOut { get; init; }
        public int RecordFailureCalls { get; private set; }
        public int UnknownAccountCalls { get; private set; }

        public Task<bool> IsLockedOutAsync(int uid, CancellationToken cancellationToken = default)
            => Task.FromResult(LockedOut);

        public Task<double?> GetRemainingLockoutSecondsAsync(int uid, CancellationToken cancellationToken = default)
            => Task.FromResult<double?>(LockedOut ? 60 : null);

        public Task<LockoutFailureResult> RecordFailureAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
        {
            RecordFailureCalls++;
            return Task.FromResult(new LockoutFailureResult(RecordFailureCalls, false));
        }

        public Task RecordUnknownAccountFailureAsync(string identifier, CancellationToken cancellationToken = default)
        {
            UnknownAccountCalls++;
            return Task.CompletedTask;
        }

        public Task ResetAsync(int uid, string email, string ip, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ClearAsync(int uid, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class StubClientIp : IClientIp
    {
        private readonly string _ip;
        public StubClientIp(string ip) => _ip = ip;
        public string? GetClientIp() => _ip;
    }
}
