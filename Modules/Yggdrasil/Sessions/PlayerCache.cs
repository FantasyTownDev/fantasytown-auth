// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Redis 玩家状态缓存实现
/// PLAYER:{uuid} → PlayerSnapshot JSON, TTL=60s
/// </summary>
public sealed class RedisPlayerCache : IPlayerCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);
    private readonly int _database;

    public RedisPlayerCache(IConnectionMultiplexer redis, int database = -1)
    {
        _redis = redis;
        _database = database;
    }

    public async ValueTask<PlayerSnapshot?> GetAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PLAYER:{uuid}";
        var value = await db.StringGetAsync(key);
        if (!value.HasValue) return null;
        return JsonSerializer.Deserialize<PlayerSnapshot>(value.ToString());
    }

    public async ValueTask SetAsync(string uuid, PlayerSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PLAYER:{uuid}";
        var json = JsonSerializer.Serialize(snapshot);
        await db.StringSetAsync(key, json, _ttl);
    }

    public async ValueTask RemoveAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"PLAYER:{uuid}";
        await db.KeyDeleteAsync(key);
    }
}

/// <summary>
/// 内存玩家状态缓存实现（用于单元测试）
/// </summary>
public sealed class InMemoryPlayerCache : IPlayerCache
{
    private readonly Dictionary<string, PlayerSnapshot> _cache = new();

    public ValueTask<PlayerSnapshot?> GetAsync(string uuid, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(uuid, out var snapshot);
        return ValueTask.FromResult(snapshot);
    }

    public ValueTask SetAsync(string uuid, PlayerSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _cache[uuid] = snapshot;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string uuid, CancellationToken cancellationToken = default)
    {
        _cache.Remove(uuid);
        return ValueTask.CompletedTask;
    }
}
