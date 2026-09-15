namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 令牌记录 - 存储在 Redis TOKEN:{accessToken} 和 ID:{email} 双键中
/// </summary>
public sealed record TokenRecord
{
    /// <summary>令牌所属用户 UID</summary>
    public required int OwnerUid { get; init; }

    /// <summary>客户端令牌（可选，用于 validate 时匹配）</summary>
    public string? ClientToken { get; init; }

    /// <summary>服务端签发的访问令牌（UUID v4 无横线）</summary>
    public required string AccessToken { get; init; }

    /// <summary>选中的玩家 profile UUID（32 位十六进制）</summary>
    public string? ProfileId { get; init; }

    /// <summary>签发时间 UTC</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>用户权限级别 (byte)</summary>
    public required byte Role { get; init; }
}
