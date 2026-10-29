// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

public class PasswordResetHandlerTests
{
    private readonly IPasswordService _passwordService = new Argon2PasswordService();

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    private PasswordResetHandler CreateHandler(AuthDbContext db, TimeSpan? resetTokenLifetime = null)
        => new(db, _passwordService, new EphemeralDataProtectionProvider(), resetTokenLifetime);

    private async Task<int> CreateTestUser(AuthDbContext db, string email = "test@example.com")
    {
        var registerHandler = new RegisterUserHandler(db, _passwordService, new InMemoryRegistrationQuotaService(), new SiteOptions(), new InlineOwnerClaimLock());
        var result = await registerHandler.HandleAsync(new RegisterUserCommand
        {
            Username = "testuser",
            Password = "Password123!",
            Email = email,
            ClientIp = "127.0.0.1"
        });

        return result.UserId!.Value;
    }

    private async Task<string> RequestTokenAsync(PasswordResetHandler handler, string email = "test@example.com")
    {
        var request = await handler.HandleRequestAsync(new RequestPasswordResetCommand { Email = email });
        Assert.True(request.IsSuccess);
        Assert.False(string.IsNullOrEmpty(request.ResetToken));
        return request.ResetToken!;
    }

    [Fact]
    public async Task HandleRequestAsync_ExistingEmail_ReturnsSuccessWithToken()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = CreateHandler(db);
        var result = await handler.HandleRequestAsync(new RequestPasswordResetCommand
        {
            Email = "test@example.com"
        });

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.ResetToken));

        // 审计 C2：令牌是密文，绝不等于裸 securityStamp
        var user = await db.Users.SingleAsync();
        Assert.NotEqual(user.SecurityStamp, result.ResetToken);
    }

    [Fact]
    public async Task HandleRequestAsync_NonexistentEmail_ReturnsSuccessWithoutToken()
    {
        using var db = CreateInMemoryDbContext();

        var handler = CreateHandler(db);
        var result = await handler.HandleRequestAsync(new RequestPasswordResetCommand
        {
            Email = "nonexistent@example.com"
        });

        // 安全考虑：即使邮箱不存在也返回成功，且不下发任何令牌
        Assert.True(result.IsSuccess);
        Assert.Null(result.ResetToken);
    }

    [Fact]
    public async Task HandleResetAsync_ValidToken_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var handler = CreateHandler(db);
        var token = await RequestTokenAsync(handler);

        var originalUser = await db.Users.FindAsync(userId);
        var oldPasswordHash = originalUser!.Password;
        var oldStamp = originalUser.SecurityStamp;

        var result = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "NewPassword123!"
        });

        Assert.True(result.IsSuccess);

        // 同一上下文中重新查询验证更改
        db.ChangeTracker.Clear();
        var updatedUser = await db.Users.FindAsync(userId);
        Assert.NotNull(updatedUser);
        Assert.NotEqual(oldPasswordHash, updatedUser.Password);

        // 验证 security_stamp 已轮换
        Assert.NotEqual(oldStamp, updatedUser.SecurityStamp);
    }

    [Fact]
    public async Task HandleResetAsync_InvalidToken_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = CreateHandler(db);
        var result = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = "invalid_token",
            NewPassword = "NewPassword123!"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无效的重置令牌", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_WeakPassword_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = CreateHandler(db);
        var token = await RequestTokenAsync(handler);

        var result = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "short"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("密码强度不足", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_TokenCannotBeReused()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = CreateHandler(db);
        var token = await RequestTokenAsync(handler);

        // 第一次使用令牌
        var first = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "NewPassword123!"
        });
        Assert.True(first.IsSuccess);

        // 第二次使用同一个令牌应该失败（stamp 已轮换，一次性语义 V5 §4.5）
        var second = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "AnotherPassword123!"
        });

        Assert.False(second.IsSuccess);
        Assert.Equal("无效的重置令牌", second.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_ExpiredToken_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        // 极短 TTL 令牌：超过时效后解护必须失败
        var handler = CreateHandler(db, resetTokenLifetime: TimeSpan.FromMilliseconds(1));
        var token = await RequestTokenAsync(handler);

        await Task.Delay(50);

        var result = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "NewPassword123!"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无效的重置令牌", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_TamperedToken_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = CreateHandler(db);
        var token = await RequestTokenAsync(handler);

        // 篡改令牌中段字符 → 解护失败
        var tampered = string.Concat(token.AsSpan(0, 10), token[10] == 'A' ? "B" : "A", token.AsSpan(11));

        var result = await handler.HandleResetAsync(new ResetPasswordCommand
        {
            Token = tampered,
            NewPassword = "NewPassword123!"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("无效的重置令牌", result.ErrorMessage);
    }
}
