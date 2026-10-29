// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户解封处理器（V5 §7.3）
/// 副作用：解除 is_banned（清 until/reason）+ DEL PERM 快照 + 审计 unban + BAN_SIGNAL 广播。
/// 本就未封禁按幂等成功处理（WasBanned=false，不落审计、不广播，对齐 §7.3 unban 幂等声明）。
/// </summary>
public sealed class UnbanUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly IBanSignalPublisher _banSignal;

    public UnbanUserHandler(
        AuthDbContext db,
        IPermissionSnapshot permissionSnapshot,
        IBanSignalPublisher? banSignal = null)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
        _banSignal = banSignal ?? NoopBanSignalPublisher.Instance;
    }

    public async Task<UnbanUserResult> HandleAsync(
        UnbanUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "unban", cancellationToken);
        if (denial != null)
            return UnbanUserResult.Failure(denial);

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);
        if (target == null)
            return UnbanUserResult.Failure("用户已软删除");

        if (!target.IsBanned)
            return UnbanUserResult.Success(wasBanned: false);

        target.IsBanned = false;
        target.BannedReason = null;
        target.BannedUntil = null;
        await _db.SaveChangesAsync(cancellationToken);

        // DEL PERM：解封即时生效（V5 §5.2）
        await _permissionSnapshot.RemoveAsync(command.TargetUid, cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "unban",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // BAN_SIGNAL 广播（仅实际解封路径；幂等 0 行在上方提前返回不发）；失败不吞
        await _banSignal.PublishAsync(new BanSignal
        {
            Action = "unban",
            Scope = "user",
            Uid = command.TargetUid,
            Email = target.Email,
            OperatorUid = command.ActorUid,
            At = DateTime.UtcNow
        }, cancellationToken);

        return UnbanUserResult.Success(wasBanned: true);
    }
}
