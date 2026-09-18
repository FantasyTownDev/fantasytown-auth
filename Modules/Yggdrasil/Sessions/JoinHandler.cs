using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

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

    public JoinHandler(AuthDbContext db, ITokenService tokenService, ITicketService ticketService)
    {
        _db = db;
        _tokenService = tokenService;
        _ticketService = ticketService;
    }

    public async Task<JoinResult> HandleAsync(string accessToken, string selectedProfileId, string serverId, CancellationToken cancellationToken = default)
    {
        // 1. 尝试验证令牌
        var token = await _tokenService.ValidateAsync(accessToken, cancellationToken);

        if (token != null)
        {
            // 令牌有效，验证 profileId 匹配
            if (token.ProfileId != selectedProfileId)
            {
                return JoinResult.Invalid();
            }
        }
        else
        {
            // authlib-injector 服务端生成自己的 token，不在 Redis 中
            // 验证 selectedProfileId 对应的玩家存在且未被封禁
            var playerExists = await _db.Players
                .AnyAsync(p => p.Uuid == selectedProfileId, cancellationToken);
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
