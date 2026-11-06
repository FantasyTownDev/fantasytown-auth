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
using IPermissionSnapshot = FantasyTown.Auth.Modules.Accounts.Infrastructure.IPermissionSnapshot;

namespace FantasyTown.Auth.Pages.Admin.Lockouts;

/// <summary>
/// 当前锁定列表（V5 §7.6 Lockouts 页数据源，moderator 策略）：
/// auth_log 派生视图——action=lockout 且未被 lockout-cleared 抵消；
/// 剩余 TTL 估算经 Redis 单键读取。禁止 SCAN LOCKOUT:*（O(键空间) 反模式）。
/// </summary>
public class IndexModel : AdminPageModel
{
    private readonly AuthDbContext _db;
    private readonly ILockoutService _lockouts;

    public IndexModel(
        AuthDbContext db,
        ILockoutService lockouts,
        IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
        _lockouts = lockouts;
    }

    public IReadOnlyList<LockoutRow> Rows { get; private set; } = Array.Empty<LockoutRow>();

    public sealed record LockoutRow(int Uid, string? Email, DateTime LastLockoutAt, double RemainingSeconds, int FailureCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        // 派生视图：有界取最近 500 条锁定/解锁审计事件，按 uid 求最新状态
        var events = await _db.AuthLogs
            .AsNoTracking()
            .Where(l => l.Action == "lockout" || l.Action == "lockout-cleared")
            .OrderByDescending(l => l.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var latestByUid = new Dictionary<int, AuthLog>();
        foreach (var e in events)
        {
            if (!latestByUid.ContainsKey(e.SubjectId))
                latestByUid[e.SubjectId] = e;   // 每 uid 最新一条（lockout 或 lockout-cleared）
        }

        var rows = new List<LockoutRow>();
        foreach (var (uid, latest) in latestByUid)
        {
            if (latest.Action != "lockout")
                continue;   // 最新事件是解锁 → 无未抵消锁定

            var remaining = await _lockouts.GetRemainingLockoutSecondsAsync(uid, cancellationToken);
            if (remaining is not { } seconds || seconds <= 0)
                continue;   // Redis 键已过期：审计残留但实际未锁

            var email = await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.Uid == uid)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            var countText = latest.Context?
                .Split(',')
                .FirstOrDefault(p => p.StartsWith("count="))?
                .Substring("count=".Length);
            _ = int.TryParse(countText, out var failureCount);

            rows.Add(new LockoutRow(uid, email, latest.CreatedAt, seconds, failureCount));
        }

        Rows = rows.OrderBy(r => r.RemainingSeconds).ToList();
    }
}
