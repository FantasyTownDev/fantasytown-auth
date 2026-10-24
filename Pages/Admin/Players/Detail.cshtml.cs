using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Pages.Admin;

/// <summary>
/// 玩家详情 + 封禁历史 + 操作表单组（V5 §7.3 GET /admin/players/{pid}，moderator 策略）。
/// 视图投影（§7.6）：按 CanManage(操作者, 属主) + NotSelf + 档位白名单渲染操作表单；
/// 隐藏 ≠ 授权——POST 一律经管线闸门与 handler 纵深防御。
/// </summary>
public class PlayersDetailModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public PlayersDetailModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    public Player Player { get; private set; } = null!;
    public User Owner { get; private set; } = null!;
    public IReadOnlyList<PlayerBan> BanHistory { get; private set; } = Array.Empty<PlayerBan>();
    public IReadOnlyList<AuthLog> OwnerRecentLogs { get; private set; } = Array.Empty<AuthLog>();

    /// <summary>可执行封禁/解封/重置皮肤（moderator 档位）</summary>
    public bool CanModerate { get; private set; }

    /// <summary>可执行改名（owner 档位）</summary>
    public bool CanRename { get; private set; }

    public async Task<IActionResult> OnGetAsync(int pid, CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Pid == pid, cancellationToken);
        if (player == null)
            return NotFound();

        Player = player;
        Owner = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(u => u.Uid == player.Uid, cancellationToken);
        BanHistory = await _db.PlayerBans
            .AsNoTracking()
            .Where(b => b.Pid == pid)
            .OrderByDescending(b => b.BannedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        OwnerRecentLogs = await _db.AuthLogs
            .AsNoTracking()
            .Where(l => l.SubjectId == Owner.Uid)
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var actorSnapshot = await GetActorSnapshotAsync(cancellationToken);
        var actorPermission = actorSnapshot?.Permission ?? UserPermission.NormalPlayer;
        var actorBanned = actorSnapshot?.IsBanned ?? true;
        var reachable = !actorBanned
            && ManagementGuard.CanManage(actorPermission, Owner.Permission)
            && ManagementGuard.NotSelf(ActorUid ?? 0, Owner.Uid);

        CanModerate = reachable && IsModeratorTier(actorPermission);
        CanRename = reachable && IsOwnerTier(actorPermission);
        return Page();
    }
}
