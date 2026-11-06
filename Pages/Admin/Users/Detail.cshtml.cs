// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Application;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Pages.Admin.Users;

/// <summary>
/// 用户详情 + 其玩家 + 近期 auth_log + 操作表单组（V5 §7.3 GET /admin/users/{uid}，owner 策略）。
/// 视图投影（§7.6）：协管打开服主详情页时不出现任何操作按钮——
/// 按 CanManage(操作者, 目标) + NotSelf + 档位渲染；软删目标隐藏全部操作。
/// </summary>
[Authorize(Policy = AdminPolicies.Owner)]
public class UsersDetailModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public UsersDetailModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    public User Target { get; private set; } = null!;
    public IReadOnlyList<Player> TargetPlayers { get; private set; } = Array.Empty<Player>();
    public IReadOnlyList<AuthLog> RecentLogs { get; private set; } = Array.Empty<AuthLog>();

    /// <summary>可执行封禁/解封/改邮箱（owner 档位 + CanManage + NotSelf）</summary>
    public bool CanManageTarget { get; private set; }

    /// <summary>可执行权限变更/强制重置/软删（owner 档位 + CanManage + NotSelf）</summary>
    public bool CanSensitive { get; private set; }

    /// <summary>可解除锁定（moderator 白名单；CanManage 或自我自救，N1）</summary>
    public bool CanClearLockout { get; private set; }

    public bool IsModeratorTarget { get; private set; }

    public async Task<IActionResult> OnGetAsync(int uid, CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        var target = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Uid == uid, cancellationToken);
        if (target == null)
            return NotFound();

        Target = target;
        TargetPlayers = await _db.Players
            .AsNoTracking()
            .Where(p => p.Uid == uid)
            .OrderBy(p => p.Pid)
            .ToListAsync(cancellationToken);
        RecentLogs = await _db.AuthLogs
            .AsNoTracking()
            .Where(l => l.SubjectId == uid)
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        IsModeratorTarget = target.Permission == UserPermission.ServerModerator;

        var actorSnapshot = await GetActorSnapshotAsync(cancellationToken);
        var actorPermission = actorSnapshot?.Permission ?? UserPermission.NormalPlayer;
        var actorBanned = actorSnapshot?.IsBanned ?? true;
        var reachable = !actorBanned
            && ManagementGuard.CanManage(actorPermission, target.Permission)
            && ManagementGuard.NotSelf(ActorUid ?? 0, target.Uid)
            && !target.IsDeleted;

        CanManageTarget = reachable && IsOwnerTier(actorPermission);
        CanSensitive = CanManageTarget;
        CanClearLockout = !actorBanned
            && IsModeratorTier(actorPermission)
            && (ManagementGuard.CanManage(actorPermission, target.Permission)
                || ManagementGuard.NotSelf(ActorUid ?? 0, target.Uid) == false);
        return Page();
    }
}
