// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil join 处理器
/// 流程：验证令牌 → 账户封禁检查 → 生成票据 → 存储票据
/// </summary>
public sealed class JoinHandler
{
    private readonly ITokenService _tokenService;
    private readonly ITicketService _ticketService;
    private readonly IPermissionSnapshot _permissionSnapshot;

    public JoinHandler(ITokenService tokenService, ITicketService ticketService, IPermissionSnapshot permissionSnapshot)
    {
        _tokenService = tokenService;
        _ticketService = ticketService;
        _permissionSnapshot = permissionSnapshot;
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

        // 2. 账户封禁检查（PERM 快照；null 按封禁等效哨兵拒绝。玩家级封禁由 hasJoined 权威把关，H6）
        var snapshot = await _permissionSnapshot.GetAsync(token.OwnerUid, cancellationToken);
        if (snapshot == null || BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow))
        {
            return JoinResult.Invalid();
        }

        // 3. 存储票据（60s 有效）
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
