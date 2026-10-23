using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 邮箱改绑处理器（V5 §7.3）
/// 副作用：更新 users.email + verified=false（重发验证，验证流程为 P1 遗留 MailService 轨道）
/// + 吊销旧邮箱全部 Ygg 令牌（H5，令牌链以 ID:{email} 为键，旧链随改绑作废）+ 审计 email-change
/// （context 含旧值到新值）。DB UNIQUE 冲突按"邮箱已被注册"拒绝。
/// </summary>
public sealed class ChangeUserEmailHandler
{
    private readonly AuthDbContext _db;
    private readonly ITokenRevocation _tokenRevocation;

    public ChangeUserEmailHandler(AuthDbContext db, ITokenRevocation tokenRevocation)
    {
        _db = db;
        _tokenRevocation = tokenRevocation;
    }

    public async Task<ChangeUserEmailResult> HandleAsync(
        ChangeUserEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（V5 §7.2：owner 级 + CanManage + NotSelf + admin-denied 审计）
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "email-change", cancellationToken);
        if (denial != null)
            return ChangeUserEmailResult.Failure(denial);

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);
        if (target == null)
            return ChangeUserEmailResult.Failure("用户已软删除");

        if (!EmailAddress.IsValid(command.NewEmail))
            return ChangeUserEmailResult.Failure("邮箱格式无效");

        if (target.Email.Equals(command.NewEmail, StringComparison.OrdinalIgnoreCase))
            return ChangeUserEmailResult.Failure("邮箱未变化");

        var oldEmail = target.Email;
        target.Email = command.NewEmail;
        target.Verified = false;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ChangeUserEmailResult.Failure("邮箱已被注册");
        }

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "email-change",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"old={oldEmail};new={command.NewEmail}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // H5：吊销旧邮箱链上的全部令牌——令牌链键为 ID:{旧邮箱}，改绑后旧链无法再被验证（异常外抛暴露 Redis 故障）
        await _tokenRevocation.RevokeAllAsync(oldEmail, cancellationToken);

        return ChangeUserEmailResult.Success(oldEmail);
    }

    // 镜像 RegisterUserHandler 的唯一约束判定（MySQL/MariaDB 1062 Duplicate entry）
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message;
        return message != null && (message.Contains("Duplicate entry") || message.Contains("1062"));
    }
}
