using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil validate 处理器
/// 判定序：TOKEN 存在 → expire_1 窗口 → clientToken 匹配 → PERM 快照 → IsBanEffective → 204
/// </summary>
public sealed class ValidateHandler
{
    private readonly ITokenService _tokenService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly YggOptions _options;

    public ValidateHandler(ITokenService tokenService, IPermissionSnapshot permissionSnapshot, YggOptions options)
    {
        _tokenService = tokenService;
        _permissionSnapshot = permissionSnapshot;
        _options = options;
    }

    public async Task<ValidateResult> HandleAsync(string accessToken, string? clientToken, CancellationToken cancellationToken = default)
    {
        // 1. 查找令牌
        var token = await _tokenService.ValidateAsync(accessToken, cancellationToken);
        if (token == null)
        {
            return ValidateResult.Invalid();
        }

        // 2. 检查 expire_1 窗口
        if (DateTimeOffset.UtcNow - token.CreatedAt > _options.TokenExpire1)
        {
            return ValidateResult.Invalid();
        }

        // 3. 检查 clientToken 匹配（如果提供了，大小写无关）
        if (clientToken != null && token.ClientToken != null &&
            !string.Equals(token.ClientToken, clientToken, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateResult.Invalid();
        }

        // 4. 检查封禁状态
        var snapshot = await _permissionSnapshot.GetAsync(token.OwnerUid, cancellationToken);
        if (snapshot != null)
        {
            var isBanned = BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow);
            if (isBanned)
            {
                return ValidateResult.Invalid();
            }
        }

        return ValidateResult.Valid();
    }
}

public sealed record ValidateResult
{
    public bool IsValid { get; init; }

    public static ValidateResult Valid() => new() { IsValid = true };
    public static ValidateResult Invalid() => new() { IsValid = false };
}
