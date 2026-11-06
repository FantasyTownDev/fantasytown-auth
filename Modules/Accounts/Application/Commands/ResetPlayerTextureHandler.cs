// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家重置皮肤处理器（V5 §7.3）：
/// tid_skin=NULL + 更新 last_modified + 审计 texture-reset。
/// 副作用（H5）：DEL PLAYER:{uuid}——PROFILE:{uuid}:{lastModified} 载荷键随
/// last_modified 更新换键自失效，无需显式删除。
/// </summary>
public sealed class ResetPlayerTextureHandler
{
    private readonly AuthDbContext _db;
    private readonly IPlayerCacheInvalidation _playerCacheInvalidation;

    public ResetPlayerTextureHandler(AuthDbContext db, IPlayerCacheInvalidation playerCacheInvalidation)
    {
        _db = db;
        _playerCacheInvalidation = playerCacheInvalidation;
    }

    public async Task<ResetPlayerTextureResult> HandleAsync(
        ResetPlayerTextureCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "texture-reset", cancellationToken);
        if (gate.Denial != null)
            return ResetPlayerTextureResult.Failure(gate.Denial);

        var player = gate.Player!;
        var hadSkin = player.TidSkin != null;
        player.TidSkin = null;
        player.LastModified = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "texture-reset",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},had_skin={hadSkin.ToString().ToLowerInvariant()}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // 副作用（H5）：失效 PLAYER:{uuid}，重置后的材质即时反映到签名载荷
        await _playerCacheInvalidation.InvalidatePlayerAsync(player.Uuid, cancellationToken);

        return ResetPlayerTextureResult.Success(hadSkin);
    }
}
