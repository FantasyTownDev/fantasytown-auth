using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 管理员清除账户锁定处理器（V5 §7.3，moderator 策略）
/// 授权走专用判定 <see cref="ManagementGuard.CanClearLockout"/>（V5 评审 N1）：
/// 目标即本人直接放行（唯一服主被锁后的自救通道），他人须 CanManage
/// （协管不能解服主的锁）。副作用：DEL LOCKOUT:{uid} + 审计 lockout-cleared。
/// </summary>
public sealed class ClearUserLockoutHandler
{
    private readonly AuthDbContext _db;
    private readonly ILockoutService _lockoutService;

    public ClearUserLockoutHandler(AuthDbContext db, ILockoutService lockoutService)
    {
        _db = db;
        _lockoutService = lockoutService;
    }

    public async Task<ClearUserLockoutResult> HandleAsync(
        ClearUserLockoutCommand command,
        CancellationToken cancellationToken = default)
    {
        var target = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);

        if (target == null)
            return ClearUserLockoutResult.Failure("用户不存在");

        // moderator 策略白名单（禁 >=）：协管/服主/PlatformAdmin，普通玩家无管理能力
        var actor = await _db.Users
            .FirstOrDefaultAsync(u => u.Uid == command.ActorUid, cancellationToken);

        if (actor == null
            || actor.Permission is not (UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin)
            || !ManagementGuard.CanClearLockout(actor.Permission, target.Permission, command.ActorUid, command.TargetUid))
        {
            _db.AuthLogs.Add(new AuthLog
            {
                Action = "admin-denied",
                ActorId = command.ActorUid,
                SubjectId = command.TargetUid,
                Context = "lockout-clear",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return ClearUserLockoutResult.Failure("无权操作");
        }

        var wasLocked = await _lockoutService.ClearAsync(command.TargetUid, cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "lockout-cleared",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"was_locked={wasLocked.ToString().ToLowerInvariant()}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return ClearUserLockoutResult.Success(wasLocked);
    }
}
