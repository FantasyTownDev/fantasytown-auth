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
