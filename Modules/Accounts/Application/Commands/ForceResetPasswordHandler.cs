using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 强制重置密码处理器（V5 §7.3）
/// 副作用：轮换 security_stamp（旧 Cookie 会话与重置令牌一并失效）+ 审计 password-force-reset。
/// TODO（Step 5 H5 / MailService 轨道）：Lua 吊销该用户全部 Ygg 令牌（跨模块 Shared 接口）、
/// 发送重置邮件（MailService 未实现，见 P1 遗留）。管理员全程不接触明文密码。
/// </summary>
public sealed class ForceResetPasswordHandler
{
    private readonly AuthDbContext _db;

    public ForceResetPasswordHandler(AuthDbContext db)
    {
        _db = db;
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

        return ForceResetPasswordResult.Success();
    }
}
