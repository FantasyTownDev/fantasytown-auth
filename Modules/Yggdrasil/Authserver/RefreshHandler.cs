using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil refresh 处理器
/// 判定序：TOKEN 存在 → expire_2 窗口 → clientToken 匹配 → 原子换发新 Token
/// </summary>
public sealed class RefreshHandler
{
    private readonly ITokenService _tokenService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly YggOptions _options;

    public RefreshHandler(ITokenService tokenService, IPermissionSnapshot permissionSnapshot, YggOptions options)
    {
        _tokenService = tokenService;
        _permissionSnapshot = permissionSnapshot;
        _options = options;
    }

    public async Task<RefreshResult> HandleAsync(string accessToken, string? clientToken, string? newProfileId, CancellationToken cancellationToken = default)
    {
        // 1. 查找旧令牌
        var oldToken = await _tokenService.ValidateAsync(accessToken, cancellationToken);
        if (oldToken == null)
        {
            return RefreshResult.Invalid();
        }

        // 2. 检查 expire_2 窗口
        if (DateTimeOffset.UtcNow - oldToken.CreatedAt > _options.TokenExpire2)
        {
            return RefreshResult.Invalid();
        }

        // 3. 检查 clientToken 匹配（大小写无关）
        if (clientToken != null && oldToken.ClientToken != null &&
            !string.Equals(oldToken.ClientToken, clientToken, StringComparison.OrdinalIgnoreCase))
        {
            return RefreshResult.Invalid();
        }

        // 4. 检查封禁状态
        var snapshot = await _permissionSnapshot.GetAsync(oldToken.OwnerUid, cancellationToken);
        if (snapshot != null)
        {
            var isBanned = BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow);
            if (isBanned)
            {
                return RefreshResult.Invalid();
            }
        }

        // 5. 原子换发新令牌
        var newToken = await _tokenService.RefreshAsync(accessToken, clientToken, newProfileId, cancellationToken);
        if (newToken == null)
        {
            return RefreshResult.Invalid();
        }

        return RefreshResult.Success(new AuthenticateResponse
        {
            AccessToken = newToken.AccessToken,
            ClientToken = newToken.ClientToken ?? clientToken ?? Guid.NewGuid().ToString("N"),
            AvailableProfiles = new[]
            {
                new ProfileInfo { Id = newToken.ProfileId ?? "", Name = "" }
            },
            SelectedProfile = newToken.ProfileId != null
                ? new ProfileInfo { Id = newToken.ProfileId, Name = "" }
                : null
        });
    }
}

public sealed record RefreshResult
{
    public bool IsValid { get; init; }
    public AuthenticateResponse? Response { get; init; }

    public static RefreshResult Valid() => new() { IsValid = true };
    public static RefreshResult Invalid() => new() { IsValid = false };
    public static RefreshResult Success(AuthenticateResponse response) => new() { IsValid = true, Response = response };
}
