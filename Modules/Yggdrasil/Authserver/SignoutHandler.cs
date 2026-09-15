using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil signout 处理器
/// 验密通过 → Lua 原子全吊销 ID:{email} 链
/// </summary>
public sealed class SignoutHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;

    public SignoutHandler(AuthDbContext db, IPasswordService passwordService, ITokenService tokenService)
    {
        _db = db;
        _passwordService = passwordService;
        _tokenService = tokenService;
    }

    public async Task<SignoutResult> HandleAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // 1. 查找用户
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            // 用户不存在，执行等耗时操作防枚举
            _passwordService.VerifyPassword(password, string.Empty);
            return SignoutResult.Invalid();
        }

        // 2. 验证密码
        var passwordValid = _passwordService.VerifyPassword(password, user.Password);
        if (!passwordValid)
        {
            return SignoutResult.Invalid();
        }

        // 3. 原子全吊销
        await _tokenService.RevokeAllAsync(email, cancellationToken);

        return SignoutResult.Success();
    }
}

public sealed record SignoutResult
{
    public bool IsValid { get; init; }

    public static SignoutResult Success() => new() { IsValid = true };
    public static SignoutResult Invalid() => new() { IsValid = false };
}
