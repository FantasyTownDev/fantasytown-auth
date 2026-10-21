using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Pages.Admin;

/// <summary>
/// 玩家列表 + 搜索（V5 §7.3 GET /admin/players，moderator 策略）。
/// 属主列读取忽略查询过滤器（软删属主仍显示并标记，其玩家仍可被管理）。
/// </summary>
public class PlayersIndexModel : AdminPageModel
{
    private readonly AuthDbContext _db;

    public PlayersIndexModel(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
        : base(permissionSnapshot)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<PlayerRow> Rows { get; private set; } = Array.Empty<PlayerRow>();

    public sealed record PlayerRow(
        int Pid, string Name, string Uuid, bool IsBanned,
        int OwnerUid, string OwnerEmail, bool OwnerIsDeleted, UserPermission OwnerPermission);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await SetNavFlagsAsync(cancellationToken);

        var query =
            from p in _db.Players
            join u in _db.Users.IgnoreQueryFilters() on p.Uid equals u.Uid
            orderby p.Pid descending
            select new PlayerRow(
                p.Pid, p.Name, p.Uuid, p.IsBanned,
                u.Uid, u.Email, u.IsDeleted, u.Permission);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            int? pid = int.TryParse(term, out var parsedPid) ? parsedPid : null;
            query = query.Where(r =>
                r.Name.Contains(term)
                || (pid != null && r.Pid == pid)
                || r.Uuid.StartsWith(term));
        }

        Rows = await query.Take(100).ToListAsync(cancellationToken);
    }
}
