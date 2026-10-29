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
/// 玩家封禁处理器（V5 §5.3/§7.5）：
/// 事务内条件更新闸门（与 IsBanEffective 同构，V5 N3）——0 行 = 目标已处封禁生效期 → 409；
/// 成功则同事务归位旧 active 记录并插入新 player_bans（每玩家至多一条 active，V4 R2）。
/// 副作用（H5）：DEL PLAYER:{uuid} + 吊销属主全部令牌——一用户一角色，属主令牌
/// selectedProfile 即该玩家，全吊销即设计所称「吊销选中令牌」+ BAN_SIGNAL 广播。
/// </summary>
public sealed class BanPlayerHandler
{
    private readonly AuthDbContext _db;
    private readonly IPlayerCacheInvalidation _playerCacheInvalidation;
    private readonly ITokenRevocation _tokenRevocation;
    private readonly IBanSignalPublisher _banSignal;

    public BanPlayerHandler(
        AuthDbContext db,
        IPlayerCacheInvalidation playerCacheInvalidation,
        ITokenRevocation tokenRevocation,
        IBanSignalPublisher? banSignal = null)
    {
        _db = db;
        _playerCacheInvalidation = playerCacheInvalidation;
        _tokenRevocation = tokenRevocation;
        _banSignal = banSignal ?? NoopBanSignalPublisher.Instance;
    }

    public async Task<BanPlayerResult> HandleAsync(
        BanPlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行：moderator 白名单 + CanManage(对属主) + NotSelf + admin-denied 审计）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "player-ban", cancellationToken);
        if (gate.Denial != null)
            return BanPlayerResult.Failure(gate.Denial);

        // L2：命令可被 null! 直构（调用方现以 ?? "" 兜底，防御性加固）
        if ((command.Reason?.Length ?? 0) > 255)
            return BanPlayerResult.Failure("封禁理由过长（最多 255 字符）");

        var nowUtc = DateTime.UtcNow;
        if (command.Until.HasValue && command.Until.Value <= nowUtc)
            return BanPlayerResult.Failure("封禁截止时间必须晚于当前时间");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 条件更新闸门（§3.4-3）：未封禁，或临时封禁已过期（未归位）才放行——
        // 0 行 = 永久/未到期临时封禁生效中，并发双封被天然串行化（后到者 409）
        var affected = await _db.Players
            .Where(p => p.Pid == command.Pid && (
                !p.IsBanned ||
                _db.PlayerBans.Any(b => b.Pid == command.Pid && b.IsActive
                    && b.BannedUntil != null && b.BannedUntil <= nowUtc)))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsBanned, true), cancellationToken);

        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await _db.PlayerBans
                .FirstOrDefaultAsync(b => b.Pid == command.Pid && b.IsActive, cancellationToken);
            return current == null
                ? BanPlayerResult.Failure("该玩家已处于封禁生效期")
                : BanPlayerResult.Conflict(current.BannedUntil, current.BannedReason);
        }

        // 同事务：归位旧 active 记录（若有）→ 插入新 active 记录
        await _db.PlayerBans
            .Where(b => b.Pid == command.Pid && b.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), cancellationToken);

        _db.PlayerBans.Add(new PlayerBan
        {
            Pid = command.Pid,
            BannedAt = nowUtc,
            BannedBy = command.ActorUid,
            BannedReason = command.Reason,
            BannedUntil = command.Until,
            IsActive = true
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 审计 player-ban（context 含理由与 until，§7.5）
        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-ban",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},reason={command.Reason},until={(command.Until.HasValue ? command.Until.Value.ToString("O") : "permanent")}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // 副作用（H5）：先失效 PLAYER:{uuid}（PROFILE 载荷键含 lastModified，封禁不改材质无需动），
        // 再经 Shared 接口吊销属主全部令牌。失败不吞——Redis 故障必须可见，静默跳过即破坏封禁即时性。
        await _playerCacheInvalidation.InvalidatePlayerAsync(gate.Player!.Uuid, cancellationToken);
        await _tokenRevocation.RevokeAllAsync(gate.Owner!.Email, cancellationToken);

        // BAN_SIGNAL 广播（uuid/reason/until 供未来 Java Agent / 插件消费）；失败不吞
        await _banSignal.PublishAsync(new BanSignal
        {
            Action = "ban",
            Scope = "player",
            Pid = command.Pid,
            Uid = gate.Owner!.Uid,
            Uuid = gate.Player!.Uuid,
            PlayerName = gate.Player.Name,
            Email = gate.Owner.Email,
            Reason = command.Reason,
            Until = command.Until,
            OperatorUid = command.ActorUid,
            At = DateTime.UtcNow
        }, cancellationToken);

        return BanPlayerResult.Success();
    }
}
