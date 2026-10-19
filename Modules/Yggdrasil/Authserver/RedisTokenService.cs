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

    public async ValueTask<TokenRecord> IssueAsync(int uid, string email, string? clientToken, string? profileId, byte role, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_database);
        var accessToken = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var token = new TokenRecord
        {
            OwnerUid = uid,
            Email = email,
            ClientToken = clientToken,
            AccessToken = accessToken,
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
            accessToken = (RedisValue)accessToken,
            tokenKey = (RedisKey)$"TOKEN:{accessToken}",
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

        // 2. 检查 clientToken 匹配（大小写无关）
        if (clientToken != null && oldToken.ClientToken != null &&
            !string.Equals(oldToken.ClientToken, clientToken, StringComparison.OrdinalIgnoreCase))
            return null;

        var newAccessToken = Guid.NewGuid().ToString("N");
        var newToken = new TokenRecord
        {
            OwnerUid = oldToken.OwnerUid,
            Email = oldToken.Email,
            ClientToken = clientToken ?? oldToken.ClientToken,
            AccessToken = newAccessToken,
            ProfileId = newProfileId ?? oldToken.ProfileId,
            CreatedAt = DateTimeOffset.UtcNow,
            Role = oldToken.Role
        };

        var tokenJson = JsonSerializer.Serialize(newToken);
        var ttlSeconds = (int)_expire2.TotalSeconds;

        // 3. 原子换发：删旧令牌 + 重挂 ID:{email} 反向链（V5 §5.4 Lua 规格 / 审计 C4）
        if (!string.IsNullOrEmpty(oldToken.Email))
        {
            await db.ScriptEvaluateAsync(TokenLuaScripts.Refresh, new
            {
                idKey = (RedisKey)$"ID:{oldToken.Email}",
                tokenPrefix = (RedisKey)"TOKEN:",
                oldAccessToken = (RedisValue)accessToken,
                accessToken = (RedisValue)newAccessToken,
                tokenKey = (RedisKey)$"TOKEN:{newAccessToken}",
                tokenJson = (RedisValue)tokenJson,
                ttl = (RedisValue)ttlSeconds
            });
        }
        else
        {
            // 旧令牌无 email（升级前存量数据）：仅换发，不维护反向链
            await db.KeyDeleteAsync($"TOKEN:{accessToken}");
            await db.StringSetAsync($"TOKEN:{newAccessToken}", tokenJson, _expire2);
        }

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
