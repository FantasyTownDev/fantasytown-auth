// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Pages.Admin;

/// <summary>
/// 管理仪表盘（V5 §7.3 GET /admin，moderator 策略）：
/// 近期 auth_log、生效封禁统计、待处理举报占位。
/// </summary>
public class IndexModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public IndexModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    public int BannedUsers { get; private set; }
    public int BannedPlayers { get; private set; }
    public IReadOnlyList<AuthLog> RecentLogs { get; private set; } = Array.Empty<AuthLog>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        BannedUsers = await _db.Users.CountAsync(
            u => u.IsBanned && (u.BannedUntil == null || u.BannedUntil > nowUtc), cancellationToken);
        BannedPlayers = await _db.Players.CountAsync(p => p.IsBanned, cancellationToken);
        RecentLogs = await _db.AuthLogs
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
    }
}
