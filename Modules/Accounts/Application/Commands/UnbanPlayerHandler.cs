using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家解封处理器（V5 §5.3 与封禁对称）：
/// 条件更新 players.is_banned: true→false + 同事务翻转唯一 active 记录；
/// 0 行 = 目标本就未处于封禁 → 幂等成功（防错 #7）。
/// 副作用 DEL PLAYER/PROFILE 随 Step 5 H5 经 Shared 接口接入。
/// </summary>
public sealed class UnbanPlayerHandler
{
    private readonly AuthDbContext _db;

    public UnbanPlayerHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<UnbanPlayerResult> HandleAsync(
        UnbanPlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "player-unban", cancellationToken);
        if (gate.Denial != null)
            return UnbanPlayerResult.Failure(gate.Denial);

        // §5.3：条件更新与翻转唯一 active 记录必须同事务
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var affected = await _db.Players
            .Where(p => p.Pid == command.Pid && p.IsBanned)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsBanned, false), cancellationToken);

        if (affected > 0)
        {
            await _db.PlayerBans
                .Where(b => b.Pid == command.Pid && b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var priorBanCount = await _db.PlayerBans
            .CountAsync(b => b.Pid == command.Pid, cancellationToken);

        // 审计（0 行幂等路径同样落审计，context 标记 was_banned）
        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-unban",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},was_banned={(affected > 0).ToString().ToLowerInvariant()},prior_bans={priorBanCount}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // TODO(Step 5 H5)：DEL PLAYER:{uuid} / PROFILE:{uuid}——经 Shared 接口接入（审计 H5）

        return UnbanPlayerResult.Success(affected > 0, priorBanCount);
    }
}
