using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Redis Lua 脚本 - 保证令牌操作原子性
/// </summary>
public static class TokenLuaScripts
{
    /// <summary>
    /// 原子签发令牌：
    /// 1. 读取旧 ID:{email} → 如果存在则 DEL 旧 TOKEN
    /// 2. SET ID:{email} = 新 accessToken (EX ttl)
    /// 3. SET TOKEN:{accessToken} = tokenJson (EX ttl)
    /// </summary>
    public static readonly LuaScript Issue = LuaScript.Prepare(@"
local old = redis.call('GET', @idKey)
if old then
    redis.call('DEL', @tokenPrefix .. old)
end
redis.call('SET', @idKey, @accessToken, 'EX', @ttl)
redis.call('SET', @tokenKey, @tokenJson, 'EX', @ttl)
return 1
");

    /// <summary>
    /// 全吊销令牌：
    /// 1. 读取 ID:{email} → 如果存在则 DEL 旧 TOKEN
    /// 2. DEL ID:{email}
    /// </summary>
    public static readonly LuaScript RevokeAll = LuaScript.Prepare(@"
local old = redis.call('GET', @idKey)
if old then
    redis.call('DEL', @tokenPrefix .. old)
end
redis.call('DEL', @idKey)
return 1
");

    /// <summary>
    /// 按 profileId 吊销令牌：
    /// 扫描 TOKEN:* 匹配 profileId 的令牌并删除对应的 ID:{email} 链。
    /// 注意：此脚本用于玩家封禁场景，生产环境应配合 PERM 快照即时拒绝。
    /// </summary>
    public static readonly LuaScript RevokeByProfile = LuaScript.Prepare(@"
local cursor = '0'
local revoked = 0
repeat
    local result = redis.call('SCAN', cursor, 'MATCH', @tokenPrefix .. '*', 'COUNT', 100)
    cursor = result[1]
    local keys = result[2]
    for i, key in ipairs(keys) do
        local json = redis.call('GET', key)
        if json then
            if string.find(json, @profileId) then
                redis.call('DEL', key)
                revoked = revoked + 1
            end
        end
    end
until cursor == '0'
return revoked
");
}
