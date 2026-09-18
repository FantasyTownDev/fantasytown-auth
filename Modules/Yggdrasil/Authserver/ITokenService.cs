namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 令牌服务接口 - 签发、验证、刷新、吊销
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 原子签发新令牌。如果用户已有令牌，先吊销旧令牌。
    /// </summary>
    ValueTask<TokenRecord> IssueAsync(int uid, string email, string? clientToken, string? profileId, byte role, string? accessToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证令牌有效性（存在 + expire_1 窗口内）。
    /// </summary>
    ValueTask<TokenRecord?> ValidateAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新令牌（expire_2 窗口内），原子换发新令牌。
    /// </summary>
    ValueTask<TokenRecord?> RefreshAsync(string accessToken, string? clientToken, string? newProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 吊销用户的所有令牌（通过 ID:{email} 链）。
    /// </summary>
    ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// 吊销单个令牌。
    /// </summary>
    ValueTask RevokeByTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
