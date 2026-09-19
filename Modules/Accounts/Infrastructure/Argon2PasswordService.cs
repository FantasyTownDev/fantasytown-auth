using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using System.Security.Cryptography;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Argon2 密码哈希服务
/// RFC 9106 推荐参数：m=65536(64MB), t=3, p=4
/// </summary>
public sealed class Argon2PasswordService : IPasswordService
{
    private const int MemoryCost = 65536; // 64 MB
    private const int TimeCost = 3;
    private const int Lanes = 4;
    private const int HashLength = 32;
    private const int SaltSize = 16;

    // dummy hash 用于防枚举攻击（等耗时）
    private static readonly string DummyHash = CreateHashInternal("dummy_password_for_timing");

    public string HashPassword(string password)
    {
        return CreateHashInternal(password);
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
        // 检查是否使用旧参数（t=1 或 p=1），需要重新哈希
        if (passwordHash.Contains(",t=1,") || passwordHash.Contains(",p=1,"))
            return true;

        return false;
    }

    private static string CreateHashInternal(string password)
    {
        byte[] salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = TimeCost,
            MemoryCost = MemoryCost,
            Lanes = Lanes,
            Threads = Lanes,
            Password = System.Text.Encoding.UTF8.GetBytes(password),
            Salt = salt,
            HashLength = HashLength
        };

        using var argon2 = new Argon2(config);
        using SecureArray<byte> hash = argon2.Hash();
        return config.EncodeString(hash.Buffer);
    }
}
