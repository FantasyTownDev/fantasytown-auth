using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil authenticate 处理器
/// 流程：封禁前置免验密 → LOCKOUT → 验密 → 原子签发 Token
/// </summary>
public sealed class AuthenticateHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ILockoutService _lockoutService;
    private readonly ITokenService _tokenService;
    private readonly YggOptions _options;
    private readonly AuthConcurrencyLimiter _concurrencyLimiter;
    private const int MaxFailedAttempts = 5;

    public AuthenticateHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot,
        ILockoutService lockoutService,
        ITokenService tokenService,
        YggOptions options,
        AuthConcurrencyLimiter concurrencyLimiter)
    {
        _db = db;
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
            return AuthenticateResult.Forbidden("Rate limit exceeded. Too many concurrent requests.");
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
        // 1. 查找用户（仅支持邮箱）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Username, cancellationToken);

        if (user == null)
        {
            // 用户不存在，执行等耗时 dummy hash 防枚举
            await _passwordService.VerifyPasswordAsync(request.Password, string.Empty, cancellationToken);
            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 2. 检查是否被软删除
        if (user.IsDeleted)
        {
            await _passwordService.VerifyPasswordAsync(request.Password, string.Empty, cancellationToken);
            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 3. 封禁前置检查 (M9) — 不验密，节省 CPU
        var snapshot = await _permissionSnapshot.GetAsync(user.Uid, cancellationToken);
        if (snapshot != null)
        {
            var isBanned = BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow);
            if (isBanned)
            {
                return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
            }
        }

        // 4. LOCKOUT 检查（跨通道共享）
        if (await _lockoutService.IsLockedOutAsync($"USER:{user.Uid}", cancellationToken))
        {
            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 5. 时序一致的密码验证
        var passwordValid = await _passwordService.VerifyPasswordAsync(request.Password, user.Password, cancellationToken);

        if (!passwordValid)
        {
            // 密码错误，记录失败
            var failureCount = await _lockoutService.RecordFailureAsync($"USER:{user.Uid}", cancellationToken);

            // 记录 FAIL_EP 滑动窗（email:ip 维度）
            await _lockoutService.RecordFailureAsync($"EP:{request.Username}:{clientIp}", cancellationToken);

            return AuthenticateResult.Forbidden("Invalid credentials. Invalid username or password.");
        }

        // 6. 密码正确，重置失败计数
        await _lockoutService.ResetAsync($"USER:{user.Uid}", cancellationToken);
        await _lockoutService.ResetAsync($"EP:{request.Username}:{clientIp}", cancellationToken);

        // 7. 查找玩家信息
        var player = user.Player;
        if (player == null)
        {
            // 没有玩家记录，查询 Players 表
            player = await _db.Players
                .FirstOrDefaultAsync(p => p.Uid == user.Uid, cancellationToken);
        }

        // 8. 如果玩家没有 UUID，生成一个
        string? profileId = null;
        if (player != null && string.IsNullOrEmpty(player.Uuid))
        {
            var newUuid = UuidGenerator.Generate(_options.UuidAlgorithm, player.Name);
            player.Uuid = newUuid;
            await _db.SaveChangesAsync(cancellationToken);
            profileId = newUuid;
        }
        else if (player != null)
        {
            profileId = player.Uuid;
        }

        // 9. 获取该用户的所有玩家 profiles
        var profiles = await _db.Players
            .Where(p => p.Uid == user.Uid)
            .Select(p => new ProfileInfo { Id = p.Uuid, Name = p.Name })
            .ToListAsync(cancellationToken);

        // 10. 自动选择 profile（单个时自动选择）
        ProfileInfo? selectedProfile = profiles.Count == 1 ? profiles[0] : null;

        // 如果请求指定了 clientToken 且已有 selectedProfile，使用指定的
        if (request.ClientToken != null && selectedProfile == null && profiles.Count > 0)
        {
            // 如果有 profileId 匹配的，使用它
            if (profileId != null)
            {
                selectedProfile = profiles.FirstOrDefault(p => p.Id == profileId);
            }
        }

        // 11. 原子签发 Token（优先使用客户端提供的 token 标识符，authlib-injector 兼容）
        var token = await _tokenService.IssueAsync(
            uid: user.Uid,
            email: user.Email,
            clientToken: request.ClientToken,
            profileId: selectedProfile?.Id,
            role: (byte)user.Permission,
            accessToken: request.AccessToken ?? request.ClientToken,
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
