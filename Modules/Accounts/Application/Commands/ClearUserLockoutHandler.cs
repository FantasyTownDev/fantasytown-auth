// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 管理员清除账户锁定处理器（V5 §7.3，moderator 策略）
/// 授权走专用判定 <see cref="ManagementGuard.CanClearLockout"/>（V5 评审 N1）：
/// 目标即本人直接放行（唯一服主被锁后的自救通道），他人须 CanManage
/// （协管不能解服主的锁）。副作用：DEL LOCKOUT:{uid} + 审计 lockout-cleared。
/// </summary>
public sealed class ClearUserLockoutHandler
{
    private readonly AuthDbContext _db;
    private readonly ILockoutService _lockoutService;

    public ClearUserLockoutHandler(AuthDbContext db, ILockoutService lockoutService)
    {
        _db = db;
        _lockoutService = lockoutService;
    }

    public async Task<ClearUserLockoutResult> HandleAsync(
        ClearUserLockoutCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：moderator 白名单 + CanClearLockout 自我放行 + admin-denied 审计）
        var evaluation = await ClearLockoutAuthorization.EvaluateAsync(
            _db, command.ActorUid, command.TargetUid, "lockout-clear", cancellationToken);
        if (evaluation.Denial != null)
            return ClearUserLockoutResult.Failure(evaluation.Denial);

        var wasLocked = await _lockoutService.ClearAsync(command.TargetUid, cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "lockout-cleared",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"was_locked={wasLocked.ToString().ToLowerInvariant()}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return ClearUserLockoutResult.Success(wasLocked);
    }
}
