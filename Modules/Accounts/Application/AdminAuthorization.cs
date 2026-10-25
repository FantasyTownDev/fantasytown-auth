using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application;

/// <summary>
/// owner 级用户域管理操作的统一授权闸门（V5 §7.2 冻结矩阵，handler 纵深防御）。
/// Step 3 管理管线将在 HTTP 层复用同一矩阵；此处保证任何直达 handler 的路径同样被拦截。
/// </summary>
internal static class AdminAuthorization
{
    /// <summary>
    /// 校验操作者是否可对目标执行 owner 级操作：
    /// owner 层白名单（ServerOwner/PlatformAdmin，禁 >=）+ CanManage + NotSelf。
    /// 目标不存在返回"用户不存在"；任一授权条件不满足落 admin-denied 审计并返回"无权操作"。
    /// 返回 null 表示放行。
    /// </summary>
    public static async Task<string?> DenyIfUnauthorizedAsync(
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
            return "用户不存在";

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
            return "无权操作";
        }

        return null;
    }
}
