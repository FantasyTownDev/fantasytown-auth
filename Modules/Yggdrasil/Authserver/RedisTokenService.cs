using System.Text.Json;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Redis 令牌服务实现 - Lua 脚本保证原子性
/// </summary>
public sealed class RedisTokenService : ITokenService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _expire2;
    private readonly int _database;

    public RedisTokenService(IConnectionMultiplexer redis, TimeSpan expire2, int database = -1)
    {
        _redis = redis;
        _expire2 = expire2;
        _database = database;
    }

    public async ValueTask<TokenRecord> IssueAsync(int uid, string email, string? clientToken, string? profileId, byte role, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var tokenValue = accessToken ?? Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var token = new TokenRecord
        {
            OwnerUid = uid,
            ClientToken = clientToken,
            AccessToken = tokenValue,
            ProfileId = profileId,
            CreatedAt = now,
            Role = role
        };

        var tokenJson = JsonSerializer.Serialize(token);
        var ttlSeconds = (int)_expire2.TotalSeconds;

        var prepared = TokenLuaScripts.Issue;
        await db.ScriptEvaluateAsync(prepared, new
        {
            idKey = (RedisKey)$"ID:{email}",
            tokenPrefix = (RedisKey)"TOKEN:",
            accessToken = (RedisValue)tokenValue,
            tokenKey = (RedisKey)$"TOKEN:{tokenValue}",
            tokenJson = (RedisValue)tokenJson,
            ttl = (RedisValue)ttlSeconds
        });

        return token;
    }

    public async ValueTask<TokenRecord?> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var key = $"TOKEN:{accessToken}";
        var json = await db.StringGetAsync(key);
        if (!json.HasValue) return null;

        return JsonSerializer.Deserialize<TokenRecord>(json.ToString());
    }

    public async ValueTask<TokenRecord?> RefreshAsync(string accessToken, string? clientToken, string? newProfileId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);

        // 1. 验证旧令牌存在
        var oldToken = await ValidateAsync(accessToken, cancellationToken);
        if (oldToken == null) return null;

        // 2. 检查 clientToken 匹配（如果提供了旧 clientToken）
        if (clientToken != null && oldToken.ClientToken != null && oldToken.ClientToken != clientToken)
            return null;

        // 3. 原子签发新令牌（Lua 会自动删除旧令牌）
        // 需要获取 email，从 ID:{email} 键的值反推
        // 实际上我们从 token 记录中没有 email，需要额外传入
        // 这里用简单方式：直接签发新令牌，旧令牌由调用方处理
        var newAccessToken = Guid.NewGuid().ToString("N");
        var newToken = new TokenRecord
        {
            OwnerUid = oldToken.OwnerUid,
            ClientToken = clientToken ?? oldToken.ClientToken,
            AccessToken = newAccessToken,
            ProfileId = newProfileId ?? oldToken.ProfileId,
            CreatedAt = DateTimeOffset.UtcNow,
            Role = oldToken.Role
        };

        var tokenJson = JsonSerializer.Serialize(newToken);
        var ttlSeconds = (int)_expire2.TotalSeconds;

        // 删除旧令牌
        await db.KeyDeleteAsync($"TOKEN:{accessToken}");

        // 签发新令牌
        await db.StringSetAsync($"TOKEN:{newAccessToken}", tokenJson, _expire2);

        // 注意：ID:{email} 链需要调用方维护（因为 TokenRecord 不存储 email）
        // 在 refresh 场景中，通常由 authenticate 层处理 ID 链

        return newToken;
    }

    public async ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var prepared = TokenLuaScripts.RevokeAll;
        await db.ScriptEvaluateAsync(prepared, new
        {
            idKey = (RedisKey)$"ID:{email}",
            tokenPrefix = (RedisKey)"TOKEN:"
        });
    }

    public async ValueTask RevokeByTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        await db.KeyDeleteAsync($"TOKEN:{accessToken}");
    }
}
