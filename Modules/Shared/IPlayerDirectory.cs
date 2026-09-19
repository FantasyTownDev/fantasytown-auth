namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 跨模块玩家查询接口。由 Accounts 模块实现，由 Yggdrasil 模块消费。
/// 强制模块边界：Yggdrasil 永远不直接访问 AuthDbContext。
/// </summary>
public interface IPlayerDirectory
{
    /// <summary>
    /// 按 UUID 查找玩家（无横线，32 位十六进制）。
    /// 用于：hasJoined（PLAYER 缓存未命中）、join 端点、profile 组装。
    /// </summary>
    ValueTask<PlayerInfo?> FindByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按角色名查找玩家（大小写敏感，UNIQUE）。
    /// 用于：authenticate（用户名查询）、hasJoined 验证。
    /// </summary>
    ValueTask<PlayerInfo?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析用户账户的所有可用角色（玩家列表）。
    /// 用于：authenticate 响应（availableProfiles 数组）。
    /// </summary>
    ValueTask<IReadOnlyList<PlayerInfo>> GetProfilesByUserAsync(int uid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为尚无 UUID 的玩家分配 UUID。
    /// 用于：authenticate 时玩家无 UUID 的情况。
    /// UUID 算法可配置（v3 确定性或 v4 随机）。
    /// </summary>
    ValueTask<bool> AssignUuidAsync(int pid, string uuid, CancellationToken cancellationToken = default);
}

/// <summary>
/// 跨模块边界传递的玩家信息 DTO。
/// 映射 PLAYER:{uuid} 缓存快照字段。
/// </summary>
public sealed record PlayerInfo
{
    /// <summary>玩家主键</summary>
    public required int Pid { get; init; }

    /// <summary>所属用户 ID</summary>
    public required int Uid { get; init; }

    /// <summary>角色名（唯一，大小写敏感）</summary>
    public required string Name { get; init; }

    /// <summary>Mojang 风格 UUID（32 位十六进制，无横线）</summary>
    public required string Uuid { get; init; }

    /// <summary>封禁状态（冗余快速标志，权威 = 时间谓词）</summary>
    public required bool IsBanned { get; init; }

    /// <summary>
    /// 当前活跃 player_bans 记录的封禁截止时间。
    /// null = 永久封禁，非 null = 临时封禁到期时间。
    /// R2 约束保证该记录恒唯一。
    /// </summary>
    DateTimeOffset? BannedUntil { get; init; }

    /// <summary>皮肤纹理 ID（null = 无皮肤）</summary>
    public int? TidSkin { get; init; }

    /// <summary>最后修改时间戳（皮肤变更信号，驱动签名缓存失效）</summary>
    public required DateTimeOffset LastModified { get; init; }
}
