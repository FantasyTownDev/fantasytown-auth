using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户解封处理器（V5 §7.3）
/// 副作用：解除 is_banned（清 until/reason）+ DEL PERM 快照 + 审计 unban。
/// 本就未封禁按幂等成功处理（WasBanned=false，不落审计，对齐 §7.3 unban 幂等声明）。
/// </summary>
public sealed class UnbanUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;

    public UnbanUserHandler(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
    }

    public async Task<UnbanUserResult> HandleAsync(
        UnbanUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "unban", cancellationToken);
        if (denial != null)
            return UnbanUserResult.Failure(denial);

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);
        if (target == null)
            return UnbanUserResult.Failure("用户已软删除");

        if (!target.IsBanned)
            return UnbanUserResult.Success(wasBanned: false);

        target.IsBanned = false;
        target.BannedReason = null;
        target.BannedUntil = null;
        await _db.SaveChangesAsync(cancellationToken);

        // DEL PERM：解封即时生效（V5 §5.2）
        await _permissionSnapshot.RemoveAsync(command.TargetUid, cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "unban",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return UnbanUserResult.Success(wasBanned: true);
    }
}
