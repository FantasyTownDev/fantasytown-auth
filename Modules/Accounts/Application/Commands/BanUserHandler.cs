using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户封禁处理器（V5 §7.3）
/// 副作用：置 is_banned（含 until/reason）+ DEL PERM 快照（Web 会话经 5.2 快照与拦截中间件即时失效）
/// + 审计 ban。重复封禁按覆盖处理（新理由/截止时间生效），每次落审计。
/// Lua 吊销 Ygg 令牌属跨模块副作用，随 Step 5 H5 统一接 Shared 接口。
/// </summary>
public sealed class BanUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;

    public BanUserHandler(AuthDbContext db, IPermissionSnapshot permissionSnapshot)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
    }

    public async Task<BanUserResult> HandleAsync(
        BanUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "ban", cancellationToken);
        if (denial != null)
            return BanUserResult.Failure(denial);

        // 全局过滤器：软删账户无封禁意义（哨兵快照已等效封禁）
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);
        if (target == null)
            return BanUserResult.Failure("用户已软删除");

        target.IsBanned = true;
        target.BannedReason = command.Reason;
        target.BannedUntil = command.Until;
        await _db.SaveChangesAsync(cancellationToken);

        // DEL PERM：冻结键空间 PERM:{uid}，封禁即时生效（V5 §5.2）
        await _permissionSnapshot.RemoveAsync(command.TargetUid, cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "ban",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"reason={command.Reason ?? "none"};until={(command.Until?.ToString("O") ?? "permanent")}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return BanUserResult.Success();
    }
}
