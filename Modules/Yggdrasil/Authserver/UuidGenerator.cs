using System.Security.Cryptography;
using System.Text;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// UUID 生成服务 - v3 确定性（基于用户名）或 v4 随机
/// </summary>
public static class UuidGenerator
{
    /// <summary>
    /// 生成 UUID（根据算法配置）
    /// </summary>
    /// <param name="algorithm">"v3" 或 "v4"</param>
    /// <param name="username">用户名（v3 算法需要）</param>
    /// <returns>32 位十六进制 UUID（无横线）</returns>
    public static string Generate(string algorithm, string? username = null)
    {
        return algorithm.ToLowerInvariant() switch
        {
            "v3" => GenerateV3(username ?? throw new ArgumentNullException(nameof(username), "v3 algorithm requires username")),
            "v4" => GenerateV4(),
            _ => throw new ArgumentException($"Unsupported UUID algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// UUID v3 - 基于用户名的确定性 UUID（MD5 哈希）
    /// 同一用户名始终生成相同 UUID
    /// </summary>
    public static string GenerateV3(string username)
    {
        // 使用固定命名空间 + 用户名生成确定性 UUID
        // 命名空间: "minecraft-offline-player" (自定义)
        var namespaceBytes = Encoding.UTF8.GetBytes("minecraft-offline-player");
        var nameBytes = Encoding.UTF8.GetBytes(username.ToLowerInvariant());

        // MD5(namespace + name)
        var combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

        var hash = MD5.HashData(combined);

        // 设置版本 (3) 和变体 (RFC 4122) 位
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // 版本 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // 变体 RFC 4122

        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    /// <summary>
    /// UUID v4 - 随机 UUID
    /// </summary>
    public static string GenerateV4()
    {
        var uuid = Guid.NewGuid();
        return uuid.ToString("N").ToUpperInvariant();
    }
}
