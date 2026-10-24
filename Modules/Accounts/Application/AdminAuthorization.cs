using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application;

/// <summary>
/// 管理闸门评估结果：Denial 为 null 表示放行，此时 Actor/Target 已从 DB 加载
/// （供 ★ 二次确认直接复用 actor 哈希/邮箱，避免重复查询）。
/// </summary>
internal sealed record AuthorizationEvaluation(string? Denial, User? Actor, User? Target);

/// <summary>
/// owner 级用户域管理操作的统一授权闸门（V5 §7.2 冻结矩阵，handler 纵深防御）。
/// Step 3 管理管线在 HTTP 层经 <see cref="EvaluateAsync"/> 复用同一矩阵；
/// 此处保证任何直达 handler 的路径同样被拦截。
/// </summary>
internal static class AdminAuthorization
{
    /// <summary>
    /// 矩阵评估（管线阶段三入口）：owner 层白名单（ServerOwner/PlatformAdmin，禁 >=）
    /// + CanManage + NotSelf。目标不存在返回"用户不存在"（无审计，同 handler 语义）；
    /// 任一授权条件不满足落 admin-denied 审计并返回"无权操作"。
    /// Denial 为 null 表示放行，Actor/Target 携带已加载实体。
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

        // actor 走全局过滤器：软删账户即刻丧失一切管理能力
        var actor = await db.Users
            .FirstOrDefaultAsync(u => u.Uid == actorUid, cancellationToken);

        if (actor == null
            || actor.Permission is not (UserPermission.ServerOwner or UserPermission.PlatformAdmin)
            || !ManagementGuard.CanManage(actor.Permission, target.Permission)
            || !ManagementGuard.NotSelf(actorUid, targetUid))
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

    /// <summary>
    /// handler 纵深防御闸门：仅返回拒绝文案（null 表示放行），语义与重构前一致。
    /// </summary>
    public static async Task<string?> DenyIfUnauthorizedAsync(
        AuthDbContext db,
        int actorUid,
        int targetUid,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateAsync(db, actorUid, targetUid, operation, cancellationToken);
        return evaluation.Denial;
    }
}
