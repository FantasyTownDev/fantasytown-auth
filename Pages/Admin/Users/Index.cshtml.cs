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
/// 用户列表 + 邮箱搜索（V5 §7.3 GET /admin/users，owner 策略收紧）。
/// 忽略查询过滤器：封禁/软删用户可见并标记（管理视图不隐藏事实）。
/// </summary>
[Authorize(Policy = AdminPolicies.Owner)]
public class UsersIndexModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public UsersIndexModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<UserRow> Rows { get; private set; } = Array.Empty<UserRow>();

    public sealed record UserRow(
        int Uid, string Email, UserPermission Permission,
        bool IsBanned, bool IsDeleted, int PlayerCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        var query =
            from u in _db.Users.IgnoreQueryFilters()
            orderby u.Uid descending
            select new UserRow(
                u.Uid, u.Email, u.Permission, u.IsBanned, u.IsDeleted,
                _db.Players.Count(p => p.Uid == u.Uid));

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(r => r.Email.Contains(term));
        }

        Rows = await query.Take(100).ToListAsync(cancellationToken);
    }
}
