using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Queries;

/// <summary>
/// 登录查询处理器
/// </summary>
public sealed class LoginHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ILockoutService _lockoutService;
    private const int MaxFailedAttempts = 5;

    public LoginHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot,
        ILockoutService? lockoutService = null)
    {
        _db = db;
        _passwordService = passwordService;
        _permissionSnapshot = permissionSnapshot;
        _lockoutService = lockoutService ?? new InMemoryLockoutService();
    }

    /// <summary>
    /// 处理登录查询
    /// </summary>
    public async Task<LoginResult> HandleAsync(LoginQuery query, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;

        // 1. 检查 IP 锁定
        if (await _lockoutService.IsLockedOutAsync($"IP:{query.ClientIp}", cancellationToken))
        {
            var remaining = await _lockoutService.GetRemainingLockoutSecondsAsync($"IP:{query.ClientIp}", cancellationToken);
            return LoginResult.Locked(remaining ?? 0);
        }

        // 2. 查找用户（支持邮箱或玩家名登录）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == query.Username, cancellationToken);

        if (user == null)
        {
            user = await _db.Users
                .Include(u => u.Player)
                .FirstOrDefaultAsync(u => u.Player != null && u.Player.Name == query.Username, cancellationToken);
        }

        if (user == null)
        {
            // 用户不存在，记录失败并执行等耗时操作防枚举
            await _lockoutService.RecordFailureAsync($"IP:{query.ClientIp}", cancellationToken);
            await _passwordService.VerifyPasswordAsync(query.Password, string.Empty, cancellationToken);
            return LoginResult.Failure("用户名或密码错误");
        }

        // 3. 检查是否被软删除
        if (user.IsDeleted)
        {
            return LoginResult.Failure("账户不存在");
        }

        // 4. 检查是否被封禁（时间谓词）
        var banEffective = BanRules.IsBanEffective(user.IsBanned, user.BannedUntil, nowUtc);
        if (banEffective)
        {
            var remainingSeconds = user.BannedUntil.HasValue
                ? BanRules.GetBanRemainingSeconds(user.BannedUntil, nowUtc) ?? 0
                : -1; // -1 = 永久封禁

            return LoginResult.Banned(remainingSeconds, user.BannedReason);
        }

        // 5. 检查用户级锁定
        if (await _lockoutService.IsLockedOutAsync($"USER:{user.Uid}", cancellationToken))
        {
            var remaining = await _lockoutService.GetRemainingLockoutSecondsAsync($"USER:{user.Uid}", cancellationToken);
            return LoginResult.Locked(remaining ?? 0);
        }

        // 6. 验证密码
        var passwordValid = await _passwordService.VerifyPasswordAsync(query.Password, user.Password, cancellationToken);

        if (!passwordValid)
        {
            // 密码错误，记录失败
            var failureCount = await _lockoutService.RecordFailureAsync($"IP:{query.ClientIp}", cancellationToken);
            await _lockoutService.RecordFailureAsync($"USER:{user.Uid}", cancellationToken);

            return LoginResult.Failure("用户名或密码错误");
        }

        // 7. 密码正确，重置失败计数
        await _lockoutService.ResetAsync($"IP:{query.ClientIp}", cancellationToken);
        await _lockoutService.ResetAsync($"USER:{user.Uid}", cancellationToken);

        // 8. 更新权限快照
        await _permissionSnapshot.SetAsync(user.Uid, new PermissionSnapshot
        {
            Permission = user.Permission,
            IsBanned = false,
            CreatedAt = nowUtc
        }, cancellationToken);

        // 9. 如果之前被封禁但已过期，清除封禁状态
        if (user.IsBanned && user.BannedUntil.HasValue && user.BannedUntil.Value <= nowUtc)
        {
            user.IsBanned = false;
            user.BannedUntil = null;
            user.BannedReason = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var displayName = user.Player?.Name ?? user.Email;
        return LoginResult.Success(user.Uid, displayName, user.Permission);
    }
}
