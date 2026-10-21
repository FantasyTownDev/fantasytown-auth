using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家改名处理器（V5 §7.3）：
/// 角色名规范校验（与注册同规则）+ DB UNIQUE 冲突捕获（防错 #16：直接更新捕获 1062，无预查）
/// + 更新 last_modified + 审计 player-rename（context 含旧名到新名）；uuid 不变（映射稳定）。
/// 副作用 DEL PLAYER/PROFILE/NAME:{旧名} 随 Step 5 H5 经 Shared 接口接入。
/// </summary>
public sealed class RenamePlayerHandler
{
    private readonly AuthDbContext _db;

    public RenamePlayerHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<RenamePlayerResult> HandleAsync(
        RenamePlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家改名行：owner 策略）
        var gate = await PlayerAuthorization.EvaluateOwnerTierAsync(
            _db, command.ActorUid, command.Pid, "player-rename", cancellationToken);
        if (gate.Denial != null)
            return RenamePlayerResult.Failure(gate.Denial);

        var player = gate.Player!;
        var oldName = player.Name;

        if (!PlayerNameRules.IsValid(command.NewName))
            return RenamePlayerResult.Failure("角色名不符合规范（3-16 位字母/数字/下划线）");

        if (string.Equals(oldName, command.NewName, StringComparison.Ordinal))
            return RenamePlayerResult.Failure("角色名未变化");

        player.Name = command.NewName;
        player.LastModified = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // players.name UNIQUE：并发/竞态下的占位冲突直接落库判定（防错 #16）
            return RenamePlayerResult.Failure("角色名已被占用");
        }

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-rename",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},old={oldName},new={command.NewName}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // TODO(Step 5 H5)：DEL PLAYER:{uuid} / PROFILE:{uuid} / NAME:{旧名}——经 Shared 接口接入（审计 H5）

        return RenamePlayerResult.Success(oldName);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("Duplicate entry") == true
        || ex.InnerException?.Message.Contains("1062") == true;
}
