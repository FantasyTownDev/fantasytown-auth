// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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

        // 4. 检查封禁状态（null 快照按封禁等效哨兵拒绝，C3 / V5 N2）
        var snapshot = await _permissionSnapshot.GetAsync(oldToken.OwnerUid, cancellationToken);
        if (snapshot == null || BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow))
        {
            return RefreshResult.Invalid();
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
