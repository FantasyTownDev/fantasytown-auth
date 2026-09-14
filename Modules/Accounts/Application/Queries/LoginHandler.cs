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
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public LoginHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot)
    {
        _db = db;
        _passwordService = passwordService;
        _permissionSnapshot = permissionSnapshot;
    }

    /// <summary>
    /// 处理登录查询
    /// </summary>
    public async Task<LoginResult> HandleAsync(LoginQuery query, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;

        // 1. 查找用户（支持邮箱或玩家名登录）
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
            // 用户不存在，执行等耗时操作防枚举
            await _passwordService.VerifyPasswordAsync(query.Password, string.Empty, cancellationToken);
            return LoginResult.Failure("用户名或密码错误");
        }

        // 2. 检查是否被软删除
        if (user.IsDeleted)
        {
            return LoginResult.Failure("账户不存在");
        }

        // 3. 检查是否被封禁（时间谓词）
        var banEffective = BanRules.IsBanEffective(user.IsBanned, user.BannedUntil, nowUtc);
        if (banEffective)
        {
            var remainingSeconds = user.BannedUntil.HasValue
                ? BanRules.GetBanRemainingSeconds(user.BannedUntil, nowUtc) ?? 0
                : double.MaxValue;

            return LoginResult.Banned(remainingSeconds, user.BannedReason);
        }

        // 4. 验证密码
        var passwordValid = await _passwordService.VerifyPasswordAsync(query.Password, user.Password, cancellationToken);

        if (!passwordValid)
        {
            return LoginResult.Failure("用户名或密码错误");
        }

        // 5. 密码正确，更新权限快照
        await _permissionSnapshot.SetAsync(user.Uid, new PermissionSnapshot
        {
            Permission = user.Permission,
            IsBanned = false,
            CreatedAt = nowUtc
        }, cancellationToken);

        // 6. 如果之前被封禁但已过期，清除封禁状态
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
