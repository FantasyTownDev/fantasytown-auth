// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家解封处理器（V5 §5.3 与封禁对称）：
/// 条件更新 players.is_banned: true→false + 同事务翻转唯一 active 记录；
/// 0 行 = 目标本就未处于封禁 → 幂等成功（防错 #7）。
/// 副作用（H5）：DEL PLAYER:{uuid}——解封即时反映到 join/hasJoined 封禁闸门；
/// 实际解封（affected>0）时广播 BAN_SIGNAL，幂等 0 行不广播。
/// </summary>
public sealed class UnbanPlayerHandler
{
    private readonly AuthDbContext _db;
    private readonly IPlayerCacheInvalidation _playerCacheInvalidation;
    private readonly IBanSignalPublisher _banSignal;

    public UnbanPlayerHandler(
        AuthDbContext db,
        IPlayerCacheInvalidation playerCacheInvalidation,
        IBanSignalPublisher? banSignal = null)
    {
        _db = db;
        _playerCacheInvalidation = playerCacheInvalidation;
        _banSignal = banSignal ?? NoopBanSignalPublisher.Instance;
    }

    public async Task<UnbanPlayerResult> HandleAsync(
        UnbanPlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "player-unban", cancellationToken);
        if (gate.Denial != null)
            return UnbanPlayerResult.Failure(gate.Denial);

        // §5.3：条件更新与翻转唯一 active 记录必须同事务
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var affected = await _db.Players
            .Where(p => p.Pid == command.Pid && p.IsBanned)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsBanned, false), cancellationToken);

        if (affected > 0)
        {
            await _db.PlayerBans
                .Where(b => b.Pid == command.Pid && b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var priorBanCount = await _db.PlayerBans
            .CountAsync(b => b.Pid == command.Pid, cancellationToken);

        // 审计（0 行幂等路径同样落审计，context 标记 was_banned）
        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-unban",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},was_banned={(affected > 0).ToString().ToLowerInvariant()},prior_bans={priorBanCount}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // 副作用（H5）：失效 PLAYER:{uuid}，解封即时反映到 join/hasJoined 封禁闸门
        await _playerCacheInvalidation.InvalidatePlayerAsync(gate.Player!.Uuid, cancellationToken);

        // BAN_SIGNAL 广播：仅实际解封（affected>0）——幂等 0 行无状态变化不发；失败不吞
        if (affected > 0)
        {
            await _banSignal.PublishAsync(new BanSignal
            {
                Action = "unban",
                Scope = "player",
                Pid = command.Pid,
                Uid = gate.Owner!.Uid,
                Uuid = gate.Player!.Uuid,
                PlayerName = gate.Player.Name,
                Email = gate.Owner.Email,
                OperatorUid = command.ActorUid,
                At = DateTime.UtcNow
            }, cancellationToken);
        }

        return UnbanPlayerResult.Success(affected > 0, priorBanCount);
    }
}
