namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 玩家状态缓存接口 — hasJoined 零 DB 的关键
/// Redis 键：PLAYER:{uuid} → name + lastModified + isBanned + bannedUntil, TTL=60s
/// </summary>
public interface IPlayerCache
{
    /// <summary>
    /// 获取玩家缓存（null = 缓存未命中，需回源）
    /// </summary>
    ValueTask<PlayerSnapshot?> GetAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置玩家缓存
    /// </summary>
    ValueTask SetAsync(string uuid, PlayerSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除玩家缓存（封禁/改名时主动失效）
    /// </summary>
    ValueTask RemoveAsync(string uuid, CancellationToken cancellationToken = default);
}

/// <summary>
/// 玩家状态快照
/// </summary>
public sealed record PlayerSnapshot
{
    /// <summary>玩家名</summary>
    public required string Name { get; init; }

    /// <summary>最后修改时间 UTC ticks</summary>
    public required long LastModified { get; init; }

    /// <summary>是否被封禁</summary>
    public required bool IsBanned { get; init; }

    /// <summary>封禁截止时间（null = 永久封禁）</summary>
    public DateTime? BannedUntil { get; init; }

    /// <summary>纹理 URL（HTTPS）</summary>
    public string? TexturesUrl { get; init; }

    /// <summary>纹理签名</summary>
    public string? TexturesSignature { get; init; }
}
