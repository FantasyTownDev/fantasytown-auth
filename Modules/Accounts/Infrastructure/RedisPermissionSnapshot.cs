using System.Text.Json;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis 权限快照服务
/// </summary>
public sealed class RedisPermissionSnapshot : IPermissionSnapshot
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AuthDbContext _db;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
    private readonly int _database;

    public RedisPermissionSnapshot(IConnectionMultiplexer redis, AuthDbContext db, int database = -1)
    {
        _redis = redis;
        _db = db;
        _database = database;
    }

    public async Task<PermissionSnapshot?> GetAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PERM:PLAYER:{uid}";
        
        var cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<PermissionSnapshot>(cached.ToString());
        }

        // 回源查询数据库
        var user = await _db.Users
            .Where(u => u.Uid == uid && !u.IsDeleted)
            .Select(u => new PermissionSnapshot
            {
                Permission = u.Permission,
                IsBanned = u.IsBanned,
                BannedUntil = u.BannedUntil,
                BannedReason = u.BannedReason,
                CreatedAt = DateTime.UtcNow
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user != null)
        {
            // 缓存结果
            await db.StringSetAsync(key, JsonSerializer.Serialize(user), _cacheExpiration);
        }

        return user;
    }

    public async Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PERM:PLAYER:{uid}";
        
        await db.StringSetAsync(key, JsonSerializer.Serialize(snapshot), _cacheExpiration);
    }

    public async Task RemoveAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PERM:PLAYER:{uid}";
        
        await db.KeyDeleteAsync(key);
    }
}

/// <summary>
/// 内存权限快照服务（用于单元测试）
/// </summary>
public sealed class InMemoryPermissionSnapshot : IPermissionSnapshot
{
    private readonly Dictionary<int, PermissionSnapshot> _cache = new();

    public Task<PermissionSnapshot?> GetAsync(int uid, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(uid, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _cache[uid] = snapshot;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(int uid, CancellationToken cancellationToken = default)
    {
        _cache.Remove(uid);
        return Task.CompletedTask;
    }
}
