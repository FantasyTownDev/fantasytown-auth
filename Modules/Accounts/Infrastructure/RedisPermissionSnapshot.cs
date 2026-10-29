// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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
    // V5 §3.4 冻结 TTL：PERM:{uid} 60s（T3-012）
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);
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
        var key = $"PERM:{uid}";

        var cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            var hit = JsonSerializer.Deserialize<PermissionSnapshot>(cached.ToString());
            if (hit != null)
            {
                return hit;
            }
            // 反序列化失败按缓存未命中处理，回源重建（C3）
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

        // V5 评审 N2 哨兵语义：回源空结果 = 封禁等效哨兵（可缓存值，消除 miss-on-empty 回源风暴 / C3）
        var snapshot = user ?? new PermissionSnapshot
        {
            Permission = UserPermission.NormalPlayer,
            IsBanned = true,
            BannedUntil = null,
            CreatedAt = DateTime.UtcNow
        };

        // 缓存结果（哨兵同样入缓存，防穿透）
        await db.StringSetAsync(key, JsonSerializer.Serialize(snapshot), _cacheExpiration);

        return snapshot;
    }

    public async Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PERM:{uid}";

        await db.StringSetAsync(key, JsonSerializer.Serialize(snapshot), _cacheExpiration);
    }

    public async Task RemoveAsync(int uid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PERM:{uid}";

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
