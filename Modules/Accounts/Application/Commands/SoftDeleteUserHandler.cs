using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 软删除用户处理器
/// 行为：授权判定（审计 H1）→ is_deleted=true + 释放角色名占位 + 审计
/// 角色名改写为 deleted_{pid}_{timestamp}，释放 UNIQUE 约束
/// </summary>
public sealed class SoftDeleteUserHandler
{
    private readonly AuthDbContext _db;

    public SoftDeleteUserHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<SoftDeleteUserResult> HandleAsync(
        SoftDeleteUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Player)
            .FirstOrDefaultAsync(u => u.Uid == command.TargetUid, cancellationToken);

        if (user == null)
            return SoftDeleteUserResult.Failure("用户不存在");

        // 审计 H1：软删为 owner 级管理操作（V5 §7.3）——统一闸门（owner 层白名单 +
        // CanManage + NotSelf + admin-denied 审计），handler 纵深防御；
        // Step 3 管线在 HTTP 层另有越权矩阵闸门
        var denial = await AdminAuthorization.DenyIfUnauthorizedAsync(
            _db, command.ActorUid, command.TargetUid, "soft-delete", cancellationToken);
        if (denial != null)
            return SoftDeleteUserResult.Failure(denial);

        if (user.IsDeleted)
            return SoftDeleteUserResult.Failure("用户已软删除");

        var now = DateTime.UtcNow;
        var releasedName = $"deleted_{user.Player?.Pid}_{now.Ticks}";

        user.IsDeleted = true;

        if (user.Player != null)
        {
            user.Player.Name = releasedName;
        }

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "user-soft-delete",
            ActorId = command.ActorUid,
            SubjectId = command.TargetUid,
            Context = $"released_name={releasedName}",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return SoftDeleteUserResult.Success(releasedName);
    }
}
