namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 跨模块令牌吊销接口。由 Yggdrasil 模块实现（封装 Lua 原子全吊销），由 Accounts 管理命令消费。
/// 用途（V5 §7.3）：用户封禁/强制重置/邮箱改绑/软删后即时吊销该账户全部 Yggdrasil 令牌——
/// 管理域不得直接引用 Yggdrasil 模块类型，经此收敛（审计 H5）。
/// </summary>
public interface ITokenRevocation
{
    /// <summary>
    /// 原子吊销该账户的全部 Yggdrasil 令牌（ID:{email} 链）。
    /// 邮箱改绑场景传旧邮箱——令牌链以 email 为键，旧链随改绑消亡。
    /// </summary>
    ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default);
}
