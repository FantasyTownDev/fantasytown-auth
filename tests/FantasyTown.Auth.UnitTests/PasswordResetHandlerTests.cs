using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
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

    private async Task<int> CreateTestUser(AuthDbContext db, string email = "test@example.com")
    {
        var registerHandler = new RegisterUserHandler(db, _passwordService);
        var result = await registerHandler.HandleAsync(new RegisterUserCommand
        {
            Username = "testuser",
            Password = "Password123!",
            Email = email,
            ClientIp = "127.0.0.1"
        });

        return result.UserId!.Value;
    }

    [Fact]
    public async Task HandleRequestAsync_ExistingEmail_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = new PasswordResetHandler(db, _passwordService);
        var command = new RequestPasswordResetCommand
        {
            Email = "test@example.com"
        };

        var result = await handler.HandleRequestAsync(command);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleRequestAsync_NonexistentEmail_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();

        var handler = new PasswordResetHandler(db, _passwordService);
        var command = new RequestPasswordResetCommand
        {
            Email = "nonexistent@example.com"
        };

        var result = await handler.HandleRequestAsync(command);

        // 安全考虑：即使邮箱不存在也返回成功
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleResetAsync_ValidToken_ReturnsSuccess()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        // 获取用户的 security_stamp
        var user = await db.Users.FindAsync(userId);
        var token = user!.SecurityStamp;
        var oldPasswordHash = user.Password;

        var handler = new PasswordResetHandler(db, _passwordService);
        var command = new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "NewPassword123!"
        };

        var result = await handler.HandleResetAsync(command);

        Assert.True(result.IsSuccess);

        // 同一上下文中重新查询验证更改
        db.ChangeTracker.Clear();
        var updatedUser = await db.Users.FindAsync(userId);
        Assert.NotNull(updatedUser);
        Assert.NotEqual(oldPasswordHash, updatedUser.Password);

        // 验证 security_stamp 已轮换
        Assert.NotEqual(token, updatedUser.SecurityStamp);
    }

    [Fact]
    public async Task HandleResetAsync_InvalidToken_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        await CreateTestUser(db);

        var handler = new PasswordResetHandler(db, _passwordService);
        var command = new ResetPasswordCommand
        {
            Token = "invalid_token",
            NewPassword = "NewPassword123!"
        };

        var result = await handler.HandleResetAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("无效的重置令牌", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_WeakPassword_ReturnsFailure()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var user = await db.Users.FindAsync(userId);
        var token = user!.SecurityStamp;

        var handler = new PasswordResetHandler(db, _passwordService);
        var command = new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "short"
        };

        var result = await handler.HandleResetAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("密码强度不足", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleResetAsync_TokenCannotBeReused()
    {
        using var db = CreateInMemoryDbContext();
        var userId = await CreateTestUser(db);

        var user = await db.Users.FindAsync(userId);
        var token = user!.SecurityStamp;

        var handler = new PasswordResetHandler(db, _passwordService);

        // 第一次使用令牌
        var command1 = new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "NewPassword123!"
        };
        await handler.HandleResetAsync(command1);

        // 第二次使用同一个令牌应该失败
        var command2 = new ResetPasswordCommand
        {
            Token = token,
            NewPassword = "AnotherPassword123!"
        };
        var result = await handler.HandleResetAsync(command2);

        Assert.False(result.IsSuccess);
        Assert.Equal("无效的重置令牌", result.ErrorMessage);
    }
}
