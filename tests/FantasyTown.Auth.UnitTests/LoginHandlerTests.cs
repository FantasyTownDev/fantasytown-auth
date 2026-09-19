using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Application.Queries;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
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
        var registerHandler = new RegisterUserHandler(db, _passwordService);
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
        Assert.Equal("违规封禁", result.ErrorMessage);
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
