// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Application.Queries;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class LoginHandlerTests
{
    private readonly IPasswordService _passwordService = new Argon2PasswordService();
    private readonly IPermissionSnapshot _permissionSnapshot = new InMemoryPermissionSnapshot();

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    private async Task<int> CreateTestUser(AuthDbContext db, string username = "testuser", string email = "test@example.com", string password = "Password123!")
    {
        var registerHandler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());
        var result = await registerHandler.HandleAsync(new RegisterUserCommand
        {
            Username = username,
            Password = password,
            Email = email,
            ClientIp = "127.0.0.1"
        });

        return result.UserId!.Value;
    }

    [Fact]
    public async Task HandleAsync_ValidEmailLogin_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "test@example.com",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.UserId);
    }

    [Fact]
    public async Task HandleAsync_ValidUsernameLogin_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.UserId);
        Assert.Equal("testuser", result.Username);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "WrongPassword!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal("用户名或密码错误", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_NonexistentUser_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "nonexistent",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal("用户名或密码错误", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_BannedUser_ReturnsBanned()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        // 手动封禁用户
        var user = await db.Users.FindAsync(userId);
        user!.IsBanned = true;
        user.BannedUntil = DateTime.UtcNow.AddHours(1);
        user.BannedReason = "违规封禁";
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsBanned);
        // 验密通过后揭示：理由 + 截止期限（V5 §6.5/§4.4 评审修订）
        Assert.StartsWith("账号已被封禁：违规封禁（截止 ", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_BannedUser_WrongPassword_ReturnsUniformError()
    {
        // 防枚举：封禁 + 密码错误与正常账户同文案（不得外显封禁状态）
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var user = await db.Users.FindAsync(userId);
        user!.IsBanned = true;
        user.BannedUntil = DateTime.UtcNow.AddHours(1);
        user.BannedReason = "违规封禁";
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "wrong-password",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsBanned);
        Assert.Equal("用户名或密码错误", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_LockedUser_ReturnsUniformError()
    {
        // §8 回归：锁定状态不外显，与凭据错误同文案（持正确密码亦然）
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var lockoutService = new InMemoryLockoutService();
        await lockoutService.LockAsync(userId, TimeSpan.FromMinutes(15));

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot, lockoutService);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal("用户名或密码错误", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_PermanentBan_ReturnsBannedWithInfiniteSeconds()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var user = await db.Users.FindAsync(userId);
        user!.IsBanned = true;
        user.BannedUntil = null; // 永久封禁
        user.BannedReason = "永久封禁";
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsBanned);
        Assert.Equal(-1, result.BanRemainingSeconds);
        Assert.Equal("账号已被封禁：永久封禁（永久）", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ExpiredBan_ClearsBanAndAllowsLogin()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        // 设置已过期的封禁
        var user = await db.Users.FindAsync(userId);
        user!.IsBanned = true;
        user.BannedUntil = DateTime.UtcNow.AddHours(-1); // 已过期
        user.BannedReason = "过期封禁";
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);

        // 验证封禁状态已清除
        var updatedUser = await db.Users.FindAsync(userId);
        Assert.False(updatedUser!.IsBanned);
        Assert.Null(updatedUser.BannedUntil);
    }

    [Fact]
    public async Task HandleAsync_SoftDeletedUser_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var user = await db.Users.FindAsync(userId);
        user!.IsDeleted = true;
        await db.SaveChangesAsync();

        // 全局查询过滤器排除软删用户，等同于用户不存在
        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal("用户名或密码错误", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulLogin_UpdatesPermissionSnapshot()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var handler = new LoginHandler(db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = "testuser",
            Password = "Password123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.HandleAsync(query);

        var snapshot = await _permissionSnapshot.GetAsync(result.UserId!.Value);
        Assert.NotNull(snapshot);
        Assert.Equal(UserPermission.NormalPlayer, snapshot.Permission);
        Assert.False(snapshot.IsBanned);
    }
}
