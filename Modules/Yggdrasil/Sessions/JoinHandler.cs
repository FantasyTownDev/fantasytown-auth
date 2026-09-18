using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil join 处理器
/// 流程：验证令牌 → 生成票据 → 存储票据
/// </summary>
public sealed class JoinHandler
{
    private readonly AuthDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly ITicketService _ticketService;
    private readonly ILogger<JoinHandler> _logger;

    public JoinHandler(AuthDbContext db, ITokenService tokenService, ITicketService ticketService, ILogger<JoinHandler> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _ticketService = ticketService;
        _logger = logger;
    }

    public async Task<JoinResult> HandleAsync(string accessToken, string selectedProfileId, string serverId, CancellationToken cancellationToken = default)
    {
        // 1. 尝试验证令牌
        var token = await _tokenService.ValidateAsync(accessToken, cancellationToken);

        if (token != null)
        {
            var match = string.Equals(token.ProfileId, selectedProfileId, StringComparison.OrdinalIgnoreCase);
            _logger.LogWarning("[JOIN] Token found in Redis, profileId={ProfileId}, match={Match}", token.ProfileId, match);
            if (!match)
            {
                return JoinResult.Invalid();
            }
        }
        else
        {
            var playerExists = await _db.Players
                .AnyAsync(p => p.Uuid == selectedProfileId, cancellationToken);
            _logger.LogWarning("[JOIN] Token NOT in Redis, playerExists={Exists} for profileId={ProfileId}", playerExists, selectedProfileId);
            if (!playerExists)
            {
                return JoinResult.Invalid();
            }
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
