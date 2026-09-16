using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Redis 票据服务实现
/// SERVER:{serverId} → playerUuid, TTL=60s
/// hasJoined 时 GETDEL 保证一次性
/// </summary>
public sealed class RedisTicketService : ITicketService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);
    private readonly int _database;

    public RedisTicketService(IConnectionMultiplexer redis, int database = -1)
    {
        _redis = redis;
        _database = database;
    }

    public async ValueTask SetAsync(string serverId, string playerUuid, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"SERVER:{serverId}";
        await db.StringSetAsync(key, playerUuid, _ttl);
    }

    public async ValueTask<string?> ConsumeAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"SERVER:{serverId}";
        return await db.StringGetDeleteAsync(key);
    }
}

/// <summary>
/// 内存票据服务实现（用于单元测试）
/// </summary>
public sealed class InMemoryTicketService : ITicketService
{
    private readonly Dictionary<string, (string uuid, DateTime expiresAt)> _tickets = new();

    public ValueTask SetAsync(string serverId, string playerUuid, CancellationToken cancellationToken = default)
    {
        _tickets[serverId] = (playerUuid, DateTime.UtcNow.AddSeconds(60));
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> ConsumeAsync(string serverId, CancellationToken cancellationToken = default)
    {
        if (_tickets.TryGetValue(serverId, out var ticket) && ticket.expiresAt > DateTime.UtcNow)
        {
            _tickets.Remove(serverId);
            return ValueTask.FromResult<string?>(ticket.uuid);
        }

        _tickets.Remove(serverId);
        return ValueTask.FromResult<string?>(null);
    }
}
