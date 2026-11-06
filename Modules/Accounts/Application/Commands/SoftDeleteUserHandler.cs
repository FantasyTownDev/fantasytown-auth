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
/// 软删除用户处理器
/// 行为：授权判定（审计 H1）→ is_deleted=true + 释放角色名占位 + DEL PERM 快照 + 吊销全部 Ygg 令牌（H5）+ 审计
/// 角色名改写为 deleted_{pid}_{timestamp}，释放 UNIQUE 约束
/// </summary>
public sealed class SoftDeleteUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ITokenRevocation _tokenRevocation;

    public SoftDeleteUserHandler(
        AuthDbContext db,
        IPermissionSnapshot permissionSnapshot,
        ITokenRevocation tokenRevocation)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
        _tokenRevocation = tokenRevocation;
    }

    public async Task<SoftDeleteUserResult> HandleAsync(
        SoftDeleteUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Player)
            .FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);

        if (user == null)
            return SoftDeleteUserResult.Failure("用户不存在");

        // 审计 H1：软删为 owner 级管理操作（V5 §7.3）——统一闸门（owner 层白名单 +
        // CanManage + NotSelf + admin-denied 审计），handler 纵深防御；
        // Step 3 管线在 HTTP 层另有越权矩阵闸门
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "soft-delete", cancellationToken);
        if (denial != null)
            return SoftDeleteUserResult.Failure(denial);

        if (user.IsDeleted)
            return SoftDeleteUserResult.Failure("用户已软删除");

        var now = DateTime.UtcNow;
        var releasedName = $"deleted_{user.Player?.Pid}_{now.Ticks}";

        user.IsDeleted = true;

        if (user.Player != null)
        {
            user.Player.Name = releasedName;
        }

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "user-soft-delete",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"released_name={releasedName}",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        // DEL PERM：冻结键空间 PERM:{uid}，软删即时生效（V5 §7.3），与封禁/解封/权限变更一致
        await _permissionSnapshot.RemoveAsync(command.TargetUid, cancellationToken);

        // H5：软删吊销该账户全部 Ygg 令牌（落库成功后触发，异常外抛以暴露 Redis 故障）
        await _tokenRevocation.RevokeAllAsync(user.Email, cancellationToken);

        return SoftDeleteUserResult.Success(releasedName);
    }
}
