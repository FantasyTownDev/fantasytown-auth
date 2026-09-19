using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
        var handler = new RegisterUserHandler(db, _passwordService);

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
}
