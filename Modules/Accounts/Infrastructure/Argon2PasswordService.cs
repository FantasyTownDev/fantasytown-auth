using Isopoh.Cryptography.Argon2;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Argon2 密码哈希服务
/// </summary>
public class Argon2PasswordService : IPasswordService
{
    // dummy hash 用于防枚举攻击（等耗时）
    private static readonly string DummyHash = Argon2.Hash("dummy_password_for_timing");

    public string HashPassword(string password)
    {
        return Argon2.Hash(password);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        return Argon2.Verify(passwordHash, password);
    }

    public async Task<bool> VerifyPasswordAsync(string password, string passwordHash, CancellationToken cancellationToken = default)
    {
        // 模拟耗时操作，防枚举
        await Task.Delay(50, cancellationToken);
        
        // 如果密码哈希无效（如用户不存在），仍需执行等耗时操作
        if (string.IsNullOrEmpty(passwordHash))
        {
            _ = Argon2.Verify(DummyHash, password);
            return false;
        }
        
        return Argon2.Verify(passwordHash, password);
    }

    public bool NeedsRehash(string passwordHash)
    {
        // Argon2 哈希不需要 rehash（每次验证都是独立哈希）
        return false;
    }
}
