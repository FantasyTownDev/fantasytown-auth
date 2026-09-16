using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 签名服务接口 — RSA-SHA1 纹理签名
/// </summary>
public interface ISigningService
{
    /// <summary>
    /// 对纹理载荷进行 RSA-SHA1 签名
    /// </summary>
    string Sign(string payload);

    /// <summary>
    /// 验证签名
    /// </summary>
    bool Verify(string payload, string signature);

    /// <summary>
    /// 获取公钥（Base64 编码）
    /// </summary>
    string GetPublicKeyBase64();
}

/// <summary>
/// RSA-SHA1 签名服务实现
/// 私钥自动管理：启动时检测，缺失则自动生成
/// </summary>
public sealed class RsaSigningService : ISigningService
{
    private readonly RSA _rsa;
    private readonly string _privateKeyPath;

    public RsaSigningService(string privateKeyPath)
    {
        _privateKeyPath = privateKeyPath;
        _rsa = RSA.Create();

        if (File.Exists(privateKeyPath))
        {
            var keyBytes = File.ReadAllBytes(privateKeyPath);
            _rsa.ImportRSAPrivateKey(keyBytes, out _);
        }
        else
        {
            // 自动生成密钥对
            _rsa = RSA.Create(2048);
            var privateKeyBytes = _rsa.ExportRSAPrivateKey();

            var directory = Path.GetDirectoryName(privateKeyPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(privateKeyPath, privateKeyBytes);

            // Unix: 设置 0600 权限
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Console.WriteLine($"[SigningService] Generated new RSA key pair at {privateKeyPath}");
        }
    }

    public string Sign(string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signatureBytes = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signatureBytes);
    }

    public bool Verify(string payload, string signature)
    {
        try
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var signatureBytes = Convert.FromBase64String(signature);
            return _rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public string GetPublicKeyBase64()
    {
        var publicKeyBytes = _rsa.ExportRSAPublicKey();
        return Convert.ToBase64String(publicKeyBytes);
    }
}

/// <summary>
/// 内存签名服务实现（用于单元测试，不提供安全性）
/// </summary>
public sealed class InMemorySigningService : ISigningService
{
    public string Sign(string payload) => $"sig_{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).Take(16).Aggregate("", (a, c) => a + c)}";

    public bool Verify(string payload, string signature) => true;

    public string GetPublicKeyBase64() => "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE";
}

/// <summary>
/// 签名结果
/// </summary>
public sealed record SignedProfile
{
    /// <summary>玩家 UUID（32 位十六进制）</summary>
    public required string Uuid { get; init; }

    /// <summary>玩家名</summary>
    public required string Name { get; init; }

    /// <summary>纹理属性列表</summary>
    public required IReadOnlyList<SignedProperty> Properties { get; init; }
}

/// <summary>
/// 签名属性（name + value + signature）
/// </summary>
public sealed record SignedProperty
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Signature { get; init; }
}

/// <summary>
/// 纹理载荷
/// </summary>
public sealed record TexturePayload
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("profileId")]
    public string ProfileId { get; init; } = "";

    [JsonPropertyName("profileName")]
    public string ProfileName { get; init; } = "";

    [JsonPropertyName("textures")]
    public TextureData Textures { get; init; } = new();
}

/// <summary>
/// 纹理数据
/// </summary>
public sealed record TextureData
{
    [JsonPropertyName("SKIN")]
    public SkinData? Skin { get; init; }
}

/// <summary>
/// 皮肤数据
/// </summary>
public sealed record SkinData
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}
