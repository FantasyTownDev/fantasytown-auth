using FantasyTown.Auth.Modules.Yggdrasil.Authserver;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil join 处理器
/// 流程：验证令牌 → 生成票据 → 存储票据
/// </summary>
public sealed class JoinHandler
{
    private readonly ITokenService _tokenService;
    private readonly ITicketService _ticketService;

    public JoinHandler(ITokenService tokenService, ITicketService ticketService)
    {
        _tokenService = tokenService;
        _ticketService = ticketService;
    }

    public async Task<JoinResult> HandleAsync(string accessToken, string selectedProfileId, string serverId, CancellationToken cancellationToken = default)
    {
        // 1. 验证令牌（V5 §6.7：令牌无效或不匹配 → 403，无回退分支）
        var token = await _tokenService.ValidateAsync(accessToken, cancellationToken);

        if (token == null)
        {
            return JoinResult.Invalid();
        }

        if (!string.Equals(token.ProfileId, selectedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return JoinResult.Invalid();
        }

        // 2. 存储票据（60s 有效）
        await _ticketService.SetAsync(serverId, selectedProfileId, cancellationToken);

        return JoinResult.Success();
    }
}

public sealed record JoinResult
{
    public bool IsValid { get; init; }

    public static JoinResult Success() => new() { IsValid = true };
    public static JoinResult Invalid() => new() { IsValid = false };
}
