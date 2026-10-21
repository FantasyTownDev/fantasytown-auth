using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil signout 处理器
/// 验密通过 → Lua 原子全吊销 ID:{email} 链
/// 数据访问经 IPlayerDirectory（H5 模块纪律边界）。
/// </summary>
public sealed class SignoutHandler
{
    private readonly IPlayerDirectory _directory;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;

    public SignoutHandler(IPlayerDirectory directory, IPasswordService passwordService, ITokenService tokenService)
    {
        _directory = directory;
        _passwordService = passwordService;
        _tokenService = tokenService;
    }

    public async Task<SignoutResult> HandleAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // 1. 查找账户（软删经目录的全局查询过滤器不可见——与原 !IsDeleted 查询等价）
        var account = await _directory.FindAccountByEmailAsync(email, cancellationToken);

        if (account == null)
        {
            // 用户不存在，VerifyPasswordAsync 内部对空哈希走 dummy Argon2 + 等耗时，防枚举（T3-009）
            await _passwordService.VerifyPasswordAsync(password, string.Empty, cancellationToken);
            return SignoutResult.Invalid();
        }

        // 2. 验证密码（异步路径，与登录侧等耗时）
        var passwordValid = await _passwordService.VerifyPasswordAsync(password, account.PasswordHash, cancellationToken);
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
