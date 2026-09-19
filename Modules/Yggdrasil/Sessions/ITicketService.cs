namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 票据服务接口 — 管理 join/hasJoined 一次性票据
/// Redis 键：SERVER:{serverId} → playerUuid, TTL=60s
/// </summary>
public interface ITicketService
{
    /// <summary>
    /// 存储票据（join 时调用）
    /// </summary>
    ValueTask SetAsync(string serverId, string playerUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证并删除票据（hasJoined 时调用，一次性）
    /// </summary>
    ValueTask<string?> ConsumeAsync(string serverId, CancellationToken cancellationToken = default);
}
