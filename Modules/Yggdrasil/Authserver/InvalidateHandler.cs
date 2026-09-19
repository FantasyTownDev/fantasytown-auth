namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil invalidate 处理器
/// 直接 DEL TOKEN:{accessToken}，不验证 clientToken（Mojang 行为）
/// </summary>
public sealed class InvalidateHandler
{
    private readonly ITokenService _tokenService;

    public InvalidateHandler(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task HandleAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        await _tokenService.RevokeByTokenAsync(accessToken, cancellationToken);
    }
}
