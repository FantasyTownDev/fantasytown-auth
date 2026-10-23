using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 强制重置密码处理器（V5 §7.3）
/// 副作用：轮换 security_stamp（旧 Cookie 会话与重置令牌一并失效）
/// + 吊销全部 Ygg 令牌（H5）+ 审计 password-force-reset。
/// 发送重置邮件属 MailService 轨道（P1 遗留，未实现）。管理员全程不接触明文密码。
/// </summary>
public sealed class ForceResetPasswordHandler
{
    private readonly AuthDbContext _db;
    private readonly ITokenRevocation _tokenRevocation;

    public ForceResetPasswordHandler(AuthDbContext db, ITokenRevocation tokenRevocation)
    {
        _db = db;
        _tokenRevocation = tokenRevocation;
    }

    public async Task<ForceResetPasswordResult> HandleAsync(
        ForceResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "password-force-reset", cancellationToken);
        if (denial != null)
            return ForceResetPasswordResult.Failure(denial);

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);
        if (target == null)
            return ForceResetPasswordResult.Failure("用户已软删除");

        target.SecurityStamp = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "password-force-reset",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = "security_stamp=rotated",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // H5：强制重置即吊销该账户全部 Ygg 令牌（落库成功后触发，异常外抛以暴露 Redis 故障）
        await _tokenRevocation.RevokeAllAsync(target.Email, cancellationToken);

        return ForceResetPasswordResult.Success();
    }
}
