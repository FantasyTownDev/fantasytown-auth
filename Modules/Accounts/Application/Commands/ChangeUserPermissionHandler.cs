// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 任命/撤销协管处理器（V5 §7.3/§7.4）
/// 副作用：更新 users.permission + DEL PERM 快照（Web 会话与 Ygg 认证即时按新权限判定）
/// + permission-change 审计（context 含旧值到新值）
/// </summary>
public sealed class ChangeUserPermissionHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;

    public ChangeUserPermissionHandler(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
    }

    public async Task<ChangeUserPermissionResult> HandleAsync(
        ChangeUserPermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "permission-change", cancellationToken);
        if (denial != null)
            return ChangeUserPermissionResult.Failure(denial);

        // §7.4：仅允许 NormalPlayer ↔ ServerModerator 两个值
        if (command.NewPermission is not (UserPermission.NormalPlayer or UserPermission.ServerModerator))
        {
            _db.AuthLogs.Add(new AuthLog
            {
                Action = "admin-denied",
                ActorId = command.ActorUid,
                SubjectId = command.TargetUid,
                Context = "permission-change:illegal-role",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return ChangeUserPermissionResult.Failure("目标角色非法");
        }

        var target = await _db.Users.FirstAsync(u => u.Uid == command.TargetUid, cancellationToken);

        if (target.Permission == command.NewPermission)
            return ChangeUserPermissionResult.Failure("目标角色未变化");

        var oldPermission = target.Permission;
        target.Permission = command.NewPermission;
        await _db.SaveChangesAsync(cancellationToken);

        // DEL PERM：冻结键空间 PERM:{uid}，变更即时生效（V5 §5.2）
        await _permissionSnapshot.RemoveAsync(command.TargetUid, cancellationToken);

        // §7.4：审计 context 含旧值到新值
        _db.AuthLogs.Add(new AuthLog
        {
            Action = "permission-change",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"old={(int)oldPermission},new={(int)command.NewPermission}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeUserPermissionResult.Success(oldPermission, command.NewPermission);
    }
}
