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
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Pages.Admin;

/// <summary>
/// 协管列表（V5 §7.3 GET /admin/moderators，owner 策略；仅服主可见导航项）。
/// §7.6：按 permission=1 过滤；忽略查询过滤器以显示被封禁/软删协管的事实状态。
/// </summary>
[Authorize(Policy = AdminPolicies.Owner)]
public class ModeratorsModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public ModeratorsModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    public IReadOnlyList<User> Moderators { get; private set; } = Array.Empty<User>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        Moderators = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Permission == UserPermission.ServerModerator)
            .OrderBy(u => u.Uid)
            .ToListAsync(cancellationToken);
    }
}
