using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// IPlayerCacheInvalidation 的 Yggdrasil 实现（H5）：封装 PLAYER:{uuid} 删除，
/// 供 Accounts 玩家管理命令经 Shared 接口触发（V5 §7.3 封禁/解封/改名/重置皮肤副作用）。
/// </summary>
public sealed class PlayerCacheInvalidationAdapter : IPlayerCacheInvalidation
{
    private readonly IPlayerCache _playerCache;

    public PlayerCacheInvalidationAdapter(IPlayerCache playerCache) => _playerCache = playerCache;

    public ValueTask InvalidatePlayerAsync(string uuid, CancellationToken cancellationToken = default)
        => _playerCache.RemoveAsync(uuid, cancellationToken);
}
