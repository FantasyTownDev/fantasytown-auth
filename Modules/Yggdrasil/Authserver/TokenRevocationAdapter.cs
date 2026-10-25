using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// ITokenRevocation 的 Yggdrasil 实现（H5）：封装 ITokenService 的 Lua 原子全吊销，
/// 供 Accounts 管理命令经 Shared 接口触发（V5 §7.3 用户封禁/强制重置/邮箱改绑/软删副作用）。
/// </summary>
public sealed class TokenRevocationAdapter : ITokenRevocation
{
    private readonly ITokenService _tokenService;

    public TokenRevocationAdapter(ITokenService tokenService) => _tokenService = tokenService;

    public ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default)
        => _tokenService.RevokeAllAsync(email, cancellationToken);
}
