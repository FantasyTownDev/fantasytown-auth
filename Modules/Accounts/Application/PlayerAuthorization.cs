using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application;

/// <summary>
/// 玩家域管理闸门评估结果：Denial 为 null 表示放行，Player/Owner 已加载。
/// </summary>
internal sealed record PlayerAuthorizationEvaluation(string? Denial, User? Actor, Player? Player, User? Owner);

/// <summary>
/// 玩家域管理操作统一授权闸门（V5 §7.2 矩阵玩家两行 + §7.5 时序，handler 纵深防御）：
/// 约束对象是玩家**属主**——CanManage(actor, 属主) 且 NotSelf(actor, 属主)，
/// 即协管不能碰服主的玩家、任何人不能碰自己名下的玩家（自我管理走用户域操作）。
/// actor 走全局过滤器：软删账户即刻丧失管理能力；属主读取忽略过滤器（软删用户的玩家仍可被管理）。
/// </summary>
internal static class PlayerAuthorization
{
    /// <summary>moderator 层（封禁/解封/重置皮肤）：moderator 白名单 + CanManage(对属主) + NotSelf</summary>
    public static Task<PlayerAuthorizationEvaluation> EvaluateModeratorTierAsync(
        AuthDbContext db, int actorUid, int pid, string operation, CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            db, actorUid, pid, operation,
            actor => actor.Permission is UserPermission.ServerModerator or UserPermission.ServerOwner or UserPermission.PlatformAdmin,
            cancellationToken);

    /// <summary>owner 层（改名）：owner 白名单 + CanManage(对属主) + NotSelf</summary>
    public static Task<PlayerAuthorizationEvaluation> EvaluateOwnerTierAsync(
        AuthDbContext db, int actorUid, int pid, string operation, CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            db, actorUid, pid, operation,
            actor => actor.Permission is UserPermission.ServerOwner or UserPermission.PlatformAdmin,
            cancellationToken);

    private static async Task<PlayerAuthorizationEvaluation> EvaluateAsync(
        AuthDbContext db,
        int actorUid,
        int pid,
        string operation,
        Func<User, bool> actorAllowed,
        CancellationToken cancellationToken)
    {
        var player = await db.Players
            .FirstOrDefaultAsync(p => p.Pid == pid, cancellationToken);

        if (player == null)
            return new PlayerAuthorizationEvaluation("玩家不存在", null, null, null);

        var owner = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Uid == player.Uid, cancellationToken);

        var actor = await db.Users
            .FirstOrDefaultAsync(u => u.Uid == actorUid, cancellationToken);

        if (actor == null
            || owner == null
            || !actorAllowed(actor)
            || !ManagementGuard.CanManage(actor.Permission, owner.Permission)
            || !ManagementGuard.NotSelf(actorUid, owner.Uid))
        {
            db.AuthLogs.Add(new AuthLog
            {
                Action = "admin-denied",
                ActorId = actorUid,
                SubjectId = owner?.Uid ?? 0,
                Context = $"{operation}:pid={pid}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return new PlayerAuthorizationEvaluation("无权操作", actor, player, owner);
        }

        return new PlayerAuthorizationEvaluation(null, actor, player, owner);
    }
}
