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
/// 玩家改名处理器（V5 §7.3）：
/// 角色名规范校验（与注册同规则）+ DB UNIQUE 冲突捕获（防错 #16：直接更新捕获 1062，无预查）
/// + 更新 last_modified + 审计 player-rename（context 含旧名到新名）；uuid 不变（映射稳定）。
/// 副作用（H5）：DEL PLAYER:{uuid}——快照内含 Name，uuid 不变仍须失效；PROFILE 载荷键随
/// last_modified 换键自失效；NAME:{旧名} 反查键待 NAME 反查缓存轨道落地后在此补失效。
/// </summary>
public sealed class RenamePlayerHandler
{
    private readonly AuthDbContext _db;
    private readonly IPlayerCacheInvalidation _playerCacheInvalidation;

    public RenamePlayerHandler(AuthDbContext db, IPlayerCacheInvalidation playerCacheInvalidation)
    {
        _db = db;
        _playerCacheInvalidation = playerCacheInvalidation;
    }

    public async Task<RenamePlayerResult> HandleAsync(
        RenamePlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家改名行：owner 策略）
        var gate = await PlayerAuthorization.EvaluateOwnerTierAsync(
            _db, command.ActorUid, command.Pid, "player-rename", cancellationToken);
        if (gate.Denial != null)
            return RenamePlayerResult.Failure(gate.Denial);

        var player = gate.Player!;
        var oldName = player.Name;

        if (!PlayerNameRules.IsValid(command.NewName))
            return RenamePlayerResult.Failure("角色名不符合规范（3-16 位字母/数字/下划线）");

        if (string.Equals(oldName, command.NewName, StringComparison.Ordinal))
            return RenamePlayerResult.Failure("角色名未变化");

        player.Name = command.NewName;
        player.LastModified = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // players.name UNIQUE：并发/竞态下的占位冲突直接落库判定（防错 #16）
            return RenamePlayerResult.Failure("角色名已被占用");
        }

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-rename",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},old={oldName},new={command.NewName}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // 副作用（H5）：失效 PLAYER:{uuid}——快照内含 Name，改名后必须清除；
        // NAME:{旧名} 反查键待 NAME 反查缓存落地后在此补失效
        await _playerCacheInvalidation.InvalidatePlayerAsync(player.Uuid, cancellationToken);

        return RenamePlayerResult.Success(oldName);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("Duplicate entry") == true
        || ex.InnerException?.Message.Contains("1062") == true;
}
