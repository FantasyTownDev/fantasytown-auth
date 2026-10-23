namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 跨模块玩家缓存失效接口。由 Yggdrasil 模块实现（封装 PLAYER:{uuid} 删除），由 Accounts 管理命令消费。
/// 用途（V5 §7.3）：封禁/解封/改名/重置皮肤后立即失效 PLAYER:{uuid}——
/// join/hasJoined 的封禁闸门与签名载荷在下一次读取时回源拿最新状态（审计 H5）。
/// </summary>
public interface IPlayerCacheInvalidation
{
    /// <summary>
    /// 失效玩家状态缓存 PLAYER:{uuid}（TTL 60s 的提前清除；键缺失时为无操作）。
    /// 改名场景 uuid 不变——快照内含 Name 字段，同样须失效。
    /// </summary>
    ValueTask InvalidatePlayerAsync(string uuid, CancellationToken cancellationToken = default);
}
