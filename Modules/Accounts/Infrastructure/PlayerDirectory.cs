using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// IPlayerDirectory 的 Accounts 实现（H5：Yggdrasil 模块唯一的账号数据访问边界）。
/// 全局查询过滤器生效：软删账户不可见；玩家实体无软删语义，其软删属主不影响查询。
/// 禁止 Yggdrasil 直接访问 AuthDbContext——所有跨模块读取经此收敛。
/// </summary>
public sealed class PlayerDirectory : IPlayerDirectory
{
    private readonly AuthDbContext _db;

    public PlayerDirectory(AuthDbContext db) => _db = db;

    public async ValueTask<PlayerInfo?> FindByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var row = await BuildQuery()
            .FirstOrDefaultAsync(p => p.Uuid == uuid, cancellationToken);
        return row == null ? null : ToInfo(row);
    }

    public async ValueTask<PlayerInfo?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var row = await BuildQuery()
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
        return row == null ? null : ToInfo(row);
    }

    public async ValueTask<IReadOnlyList<PlayerInfo>> GetProfilesByUserAsync(int uid, CancellationToken cancellationToken = default)
    {
        var rows = await BuildQuery()
            .Where(p => p.Uid == uid)
            .OrderBy(p => p.Pid)
            .ToListAsync(cancellationToken);
        return rows.Select(ToInfo).ToList();
    }

    public async ValueTask<bool> AssignUuidAsync(int pid, string uuid, CancellationToken cancellationToken = default)
    {
        try
        {
            // 防错 #16：直接条件更新（仅为空 UUID 玩家分配），uuid UNIQUE 冲突落库捕获，无预查竞态
            var affected = await _db.Players
                .Where(p => p.Pid == pid && (p.Uuid == null || p.Uuid == ""))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Uuid, uuid), cancellationToken);
            return affected > 0;
        }
        catch (DbUpdateException)
        {
            return false; // uuid UNIQUE 并发冲突
        }
    }

    public async ValueTask<AccountInfo?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // 全局查询过滤器：软删账户 → null（等同不存在，调用方统一防枚举话术）
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => new AccountInfo
            {
                Uid = u.Uid,
                Email = u.Email,
                PasswordHash = u.Password,
                Verified = u.Verified,
                Permission = (byte)u.Permission
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 玩家 + 当前 active 封禁记录的单查询投影（players LEFT JOIN 当前 active player_bans，
    /// R2 约束保证 active 记录唯一）。
    /// </summary>
    private IQueryable<PlayerRow> BuildQuery() =>
        from p in _db.Players.AsNoTracking()
        select new PlayerRow
        {
            Pid = p.Pid,
            Uid = p.Uid,
            Name = p.Name,
            Uuid = p.Uuid,
            IsBanned = p.IsBanned,
            TidSkin = p.TidSkin,
            LastModified = p.LastModified,
            BannedUntil = _db.PlayerBans
                .Where(b => b.Pid == p.Pid && b.IsActive)
                .Select(b => b.BannedUntil)
                .FirstOrDefault()
        };

    private sealed class PlayerRow
    {
        public int Pid { get; init; }
        public int Uid { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Uuid { get; init; } = string.Empty;
        public bool IsBanned { get; init; }
        public int? TidSkin { get; init; }
        public DateTime LastModified { get; init; }
        public DateTime? BannedUntil { get; init; }
    }

    private static PlayerInfo ToInfo(PlayerRow row) => new()
    {
        Pid = row.Pid,
        Uid = row.Uid,
        Name = row.Name,
        Uuid = row.Uuid,
        IsBanned = row.IsBanned,
        TidSkin = row.TidSkin,
        BannedUntil = row.BannedUntil.HasValue ? AsUtc(row.BannedUntil.Value) : null,
        LastModified = AsUtc(row.LastModified)
    };

    /// <summary>DB datetime 一律按 UTC 解释（与原回源 SQL 的 new DateTimeOffset(dt, TimeSpan.Zero) 语义一致）</summary>
    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
