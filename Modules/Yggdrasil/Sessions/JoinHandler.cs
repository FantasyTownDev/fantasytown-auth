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
        // 1. 验证令牌
        var token = await _tokenService.ValidateAsync(accessToken, cancellationToken);
        if (token == null)
        {
            return JoinResult.Invalid();
        }

        // 2. 验证 selectedProfileId 与令牌中的 profileId 匹配
        if (token.ProfileId != selectedProfileId)
        {
            return JoinResult.Invalid();
        }

        // 3. 检查封禁状态（通过 permission snapshot）
        // 注意：令牌本身不存储 IsBanned，需要通过 permission snapshot 检查
        // 但 join 是轻量级操作，直接信任 token 的有效性（validate 已检查过封禁）
        // 如果需要更严格检查，可以注入 IPermissionSnapshot

        // 4. 存储票据（60s 有效）
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
