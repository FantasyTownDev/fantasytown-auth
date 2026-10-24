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
