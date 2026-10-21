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

    /// <summary>
    /// 按邮箱查找账户（大小写规则随 DB collation）。
    /// 用于：authenticate、signout 的凭据校验入口。
    /// 软删账户不可见（全局查询过滤器生效）——返回 null 等同"账户不存在"，
    /// 调用方按统一话术 + 等耗时 dummy 校验防枚举。
    /// </summary>
    ValueTask<AccountInfo?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// 跨模块边界传递的账户信息 DTO（Yggdrasil 凭据校验所需最小面）。
/// 封禁/权限判定不在此面内——统一走 PERM 快照（IPermissionSnapshot）。
/// </summary>
public sealed record AccountInfo
{
    /// <summary>账户主键</summary>
    public required int Uid { get; init; }

    /// <summary>邮箱（Yggdrasil 用户名）</summary>
    public required string Email { get; init; }

    /// <summary>密码哈希（仅用于恒定时间校验，禁止外泄）</summary>
    public required string PasswordHash { get; init; }

    /// <summary>是否已验证邮箱</summary>
    public required bool Verified { get; init; }
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
    public DateTimeOffset? BannedUntil { get; init; }

    /// <summary>皮肤纹理 ID（null = 无皮肤）</summary>
    public int? TidSkin { get; init; }

    /// <summary>最后修改时间戳（皮肤变更信号，驱动签名缓存失效）</summary>
    public required DateTimeOffset LastModified { get; init; }
}
