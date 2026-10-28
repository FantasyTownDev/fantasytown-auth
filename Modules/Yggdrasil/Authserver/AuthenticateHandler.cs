using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil authenticate 处理器
/// 流程：封禁前置判定（暂存）→ LOCKOUT → 验密 → 封禁/哨兵揭示 → 原子签发 Token
/// 数据访问经 IPlayerDirectory（H5 模块纪律边界）；封禁/权限判定经 PERM 快照。
/// V5 §6.5 评审修订：封禁详情仅向密码持有者披露——密码错误沿用统一话术，
/// 封禁账户与正常账户的凭据错误面同文案同计时（防枚举），详见 HandleCoreAsync 内注释。
/// </summary>
public sealed class AuthenticateHandler
{
    private readonly IPlayerDirectory _directory;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ILockoutService _lockoutService;
    private readonly ITokenService _tokenService;
    private readonly YggOptions _options;
    private readonly AuthConcurrencyLimiter _concurrencyLimiter;

    public AuthenticateHandler(
        IPlayerDirectory directory,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot,
        ILockoutService lockoutService,
        ITokenService tokenService,
        YggOptions options,
        AuthConcurrencyLimiter concurrencyLimiter)
    {
        _directory = directory;
        _passwordService = passwordService;
        _permissionSnapshot = permissionSnapshot;
        _lockoutService = lockoutService;
        _tokenService = tokenService;
        _options = options;
        _concurrencyLimiter = concurrencyLimiter;
    }

    public async Task<AuthenticateResult> HandleAsync(AuthenticateRequest request, string clientIp, CancellationToken cancellationToken = default)
    {
        // 0. 并发许可检查
        if (!_concurrencyLimiter.TryAcquire())
        {
            // T3-014：不向客户端泄露内部并发调度细节，统一文案
            return AuthenticateResult.Forbidden("Forbidden.");
        }

        try
        {
            return await HandleCoreAsync(request, clientIp, cancellationToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task<AuthenticateResult> HandleCoreAsync(AuthenticateRequest request, string clientIp, CancellationToken cancellationToken)
    {
        // 1. 查找账户（仅支持邮箱；软删经目录的全局查询过滤器不可见——
        //    返回 null 走统一防枚举分支，替代原先的显式 IsDeleted 检查）
        var account = await _directory.FindAccountByEmailAsync(request.Username, cancellationToken);

        if (account == null)
        {
            // 用户不存在：仅记 FAIL_ACCT 审计维度（V5 §6.6，不触发锁定），执行等耗时 dummy hash 防枚举
            await _lockoutService.RecordUnknownAccountFailureAsync(request.Username, cancellationToken);
            await _passwordService.VerifyPasswordAsync(request.Password, string.Empty, cancellationToken);
            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 3. 封禁前置判定（防错 #17：先 PERM 判定再验密；M9 回源与用户查询合并一次 DB 读）——
        //    判定结果暂存、不立即拒绝：验密通过后才揭示封禁详情（V5 §6.5 评审修订），
        //    密码错误沿用统一话术，封禁与正常账户的凭据错误面同文案同时序（防枚举，§8）。
        //    null 快照按封禁等效哨兵拒绝（C3 / V5 N2）。
        var snapshot = await _permissionSnapshot.GetAsync(account.Uid, cancellationToken);
        var banEffective = snapshot != null
            && BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow);
        var missingSnapshot = snapshot == null;

        // 4. LOCKOUT 检查（LOCKOUT:{uid}，Web 与 Ygg authenticate 共享键空间）
        if (await _lockoutService.IsLockedOutAsync(account.Uid, cancellationToken))
        {
            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 5. 时序一致的密码验证
        var passwordValid = await _passwordService.VerifyPasswordAsync(request.Password, account.PasswordHash, cancellationToken);

        if (!passwordValid)
        {
            // 记录失败：FAIL_EP:{email}:{ip}（同 IP 窗口 ≥5 触发 LOCKOUT）+ FAIL_ACCT:{email}（审计）
            await _lockoutService.RecordFailureAsync(account.Uid, account.Email, clientIp, cancellationToken);

            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 6. 密码正确，清除 LOCKOUT:{uid} 与 FAIL_EP:{email}:{ip}
        await _lockoutService.ResetAsync(account.Uid, account.Email, clientIp, cancellationToken);

        // 6.5 封禁/哨兵揭示（仅向密码持有者披露；过期封禁 IsBanEffective=false 已落入正常登录）：
        //     until 取判定同源的 PERM 快照，reason 取 account 行（同查询附带，零额外 DB 读）；
        //     PERM null 哨兵不伪造封禁理由，给可联系管理员的状态异常文案。
        if (missingSnapshot)
        {
            return AuthenticateResult.Forbidden("账号状态异常，请联系管理员");
        }
        if (banEffective)
        {
            return AuthenticateResult.Forbidden(
                BanRules.RevealMessage(snapshot!.BannedUntil, account.BannedReason));
        }

        // 7. 解析玩家（当前冻结 schema：一用户一角色，取首个；多角色为后续演进）
        var players = (await _directory.GetProfilesByUserAsync(account.Uid, cancellationToken)).ToList();
        var player = players.Count > 0 ? players[0] : null;

        // 8. 如果玩家没有 UUID，按配置算法分配（条件更新仅对空 UUID 生效）
        string? profileId = null;
        if (player != null && string.IsNullOrEmpty(player.Uuid))
        {
            var newUuid = UuidGenerator.Generate(_options.UuidAlgorithm, player.Name);
            if (await _directory.AssignUuidAsync(player.Pid, newUuid, cancellationToken))
            {
                profileId = newUuid;
                // 本地列表同步已分配 UUID，保持 availableProfiles 一致
                players = players
                    .Select(p => p.Pid == player.Pid ? p with { Uuid = newUuid } : p)
                    .ToList();
            }
            else
            {
                // 并发竞争中 UUID 已被其他请求分配（条件更新 0 行）：重取实际值
                // （原直接 SaveChanges 路径在该竞争下会抛 1062 → 500）
                players = (await _directory.GetProfilesByUserAsync(account.Uid, cancellationToken)).ToList();
                profileId = players.Count > 0 ? players[0].Uuid : null;
            }
        }
        else if (player != null)
        {
            profileId = player.Uuid;
        }

        // 9. 获取该用户的所有玩家 profiles
        var profiles = players
            .Select(p => new ProfileInfo { Id = p.Uuid, Name = p.Name })
            .ToList();

        // 10. 自动选择 profile（单个时自动选择）
        ProfileInfo? selectedProfile = profiles.Count == 1 ? profiles[0] : null;

        // 如果请求指定了 clientToken 且已有 selectedProfile，使用指定的
        if (request.ClientToken != null && selectedProfile == null && profiles.Count > 0)
        {
            // 如果有 profileId 匹配的，使用它
            if (profileId != null)
            {
                selectedProfile = profiles.FirstOrDefault(p =>
                    string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            }
        }

        // 11. 原子签发 Token（role 为账户权限元数据；授权判定仍走 PERM 快照）
        var token = await _tokenService.IssueAsync(
            uid: account.Uid,
            email: account.Email,
            clientToken: request.ClientToken,
            profileId: selectedProfile?.Id,
            role: account.Permission,
            cancellationToken);

        return AuthenticateResult.Success(new AuthenticateResponse
        {
            AccessToken = token.AccessToken,
            ClientToken = token.ClientToken ?? request.ClientToken ?? Guid.NewGuid().ToString("N"),
            AvailableProfiles = profiles,
            SelectedProfile = selectedProfile
        });
    }
}

/// <summary>
/// authenticate 处理结果
/// </summary>
public sealed record AuthenticateResult
{
    public bool IsSuccess { get; init; }
    public AuthenticateResponse? Response { get; init; }
    public YggErrorResponse? Error { get; init; }
    public int StatusCode { get; init; }

    public static AuthenticateResult Success(AuthenticateResponse response) => new()
    {
        IsSuccess = true,
        Response = response,
        StatusCode = 200
    };

    public static AuthenticateResult Forbidden(string message) => new()
    {
        IsSuccess = false,
        Error = YggErrorResponse.Forbidden(message),
        StatusCode = 403
    };
}
