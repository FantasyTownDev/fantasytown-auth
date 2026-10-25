using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application;

/// <summary>
/// 解除 LOCKOUT 专用授权闸门（V5 §7.2 / 评审 N1，moderator 策略行）：
/// 自我解锁直接放行（唯一服主被锁后的自救通道），他人须 CanManage；
/// actor 走 moderator 白名单（ServerModerator/ServerOwner/PlatformAdmin，禁 >=）。
/// handler 纵深防御 + Step 3 管线 HTTP 层共用同一实现。
/// </summary>
internal static class ClearLockoutAuthorization
{
    /// <summary>
    /// 矩阵评估：目标不存在返回"用户不存在"（无审计）；
    /// 授权不满足落 admin-denied 审计并返回"无权操作"；Denial 为 null 表示放行。
    /// </summary>
    public static async Task<AuthorizationEvaluation> EvaluateAsync(
        AuthDbContext db,
        int actorUid,
        int targetUid,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var target = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Uid == targetUid, cancellationToken);

        if (target == null)
            return new AuthorizationEvaluation("用户不存在", null, target);

        var actor = await db.Users
            .FirstOrDefaultAsync(u => u.Uid == actorUid, cancellationToken);

        if (actor == null
            || actor.Permission is not (UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin)
            || !ManagementGuard.CanClearLockout(actor.Permission, target.Permission, actorUid, targetUid))
        {
            db.AuthLogs.Add(new AuthLog
            {
                Action = "admin-denied",
                ActorId = actorUid,
                SubjectId = targetUid,
                Context = operation,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return new AuthorizationEvaluation("无权操作", actor, target);
        }

        return new AuthorizationEvaluation(null, actor, target);
    }
}
