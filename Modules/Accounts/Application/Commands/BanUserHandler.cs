using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户封禁处理器（V5 §7.3）
/// 副作用：置 is_banned（含 until/reason）+ DEL PERM 快照（Web 会话经 5.2 快照与拦截中间件即时失效）
/// + 吊销全部 Ygg 令牌（H5）+ 审计 ban + BAN_SIGNAL 广播（V5 评审：供 Agent/插件消费）。
/// 重复封禁按覆盖处理（新理由/截止时间生效），每次落审计。
/// </summary>
public sealed class BanUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ITokenRevocation _tokenRevocation;
    private readonly IBanSignalPublisher _banSignal;

    public BanUserHandler(
        AuthDbContext db,
        IPermissionSnapshot permissionSnapshot,
        ITokenRevocation tokenRevocation,
        IBanSignalPublisher? banSignal = null)
    {
        _db = db;
        _permissionSnapshot = permissionSnapshot;
        _tokenRevocation = tokenRevocation;
        _banSignal = banSignal ?? NoopBanSignalPublisher.Instance;
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

        // M2 链 b：对称 BanPlayerHandler——BannedReason VARCHAR(255)，表单 maxlength
        // 纯客户端可绕，超长走 DB 1406 会以 500 冒泡
        if (command.Reason != null && command.Reason.Length > 255)
            return BanUserResult.Failure("封禁理由过长（最多 255 字符）");

        // M4：对称 BanPlayerHandler——过去/当前的 Until 若直写会得到
        // IsBanned=true 且 IsBanEffective=false（管理页显示已封禁、实际可登录，
        // 冻结项 6 状态机歧义）
        var nowUtc = DateTime.UtcNow;
        if (command.Until.HasValue && command.Until.Value <= nowUtc)
            return BanUserResult.Failure("封禁截止时间必须晚于当前时间");

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

        // H5：封禁即时吊销该账户全部 Ygg 令牌（落库成功后触发，异常外抛以暴露 Redis 故障）
        await _tokenRevocation.RevokeAllAsync(target.Email, cancellationToken);

        // BAN_SIGNAL 广播（结构化事件，供未来 Java Agent / 插件消费）；失败不吞（同 H5 语义）
        await _banSignal.PublishAsync(new BanSignal
        {
            Action = "ban",
            Scope = "user",
            Uid = command.TargetUid,
            Email = target.Email,
            Reason = command.Reason,
            Until = command.Until,
            OperatorUid = command.ActorUid,
            At = DateTime.UtcNow
        }, cancellationToken);

        return BanUserResult.Success();
    }
}
