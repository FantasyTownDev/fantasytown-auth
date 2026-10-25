using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家封禁处理器（V5 §5.3/§7.5）：
/// 事务内条件更新闸门（与 IsBanEffective 同构，V5 N3）——0 行 = 目标已处封禁生效期 → 409；
/// 成功则同事务归位旧 active 记录并插入新 player_bans（每玩家至多一条 active，V4 R2）。
/// 副作用 DEL PLAYER/PROFILE 与令牌吊销随 Step 5 H5 经 Shared 接口接入。
/// </summary>
public sealed class BanPlayerHandler
{
    private readonly AuthDbContext _db;

    public BanPlayerHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<BanPlayerResult> HandleAsync(
        BanPlayerCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行：moderator 白名单 + CanManage(对属主) + NotSelf + admin-denied 审计）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "player-ban", cancellationToken);
        if (gate.Denial != null)
            return BanPlayerResult.Failure(gate.Denial);

        if (command.Reason.Length > 255)
            return BanPlayerResult.Failure("封禁理由过长（最多 255 字符）");

        var nowUtc = DateTime.UtcNow;
        if (command.Until.HasValue && command.Until.Value <= nowUtc)
            return BanPlayerResult.Failure("封禁截止时间必须晚于当前时间");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 条件更新闸门（§3.4-3）：未封禁，或临时封禁已过期（未归位）才放行——
        // 0 行 = 永久/未到期临时封禁生效中，并发双封被天然串行化（后到者 409）
        var affected = await _db.Players
            .Where(p => p.Pid == command.Pid && (
                !p.IsBanned ||
                _db.PlayerBans.Any(b => b.Pid == command.Pid && b.IsActive
                    && b.BannedUntil != null && b.BannedUntil <= nowUtc)))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsBanned, true), cancellationToken);

        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await _db.PlayerBans
                .FirstOrDefaultAsync(b => b.Pid == command.Pid && b.IsActive, cancellationToken);
            return current == null
                ? BanPlayerResult.Failure("该玩家已处于封禁生效期")
                : BanPlayerResult.Conflict(current.BannedUntil, current.BannedReason);
        }

        // 同事务：归位旧 active 记录（若有）→ 插入新 active 记录
        await _db.PlayerBans
            .Where(b => b.Pid == command.Pid && b.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), cancellationToken);

        _db.PlayerBans.Add(new PlayerBan
        {
            Pid = command.Pid,
            BannedAt = nowUtc,
            BannedBy = command.ActorUid,
            BannedReason = command.Reason,
            BannedUntil = command.Until,
            IsActive = true
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 审计 player-ban（context 含理由与 until，§7.5）
        _db.AuthLogs.Add(new AuthLog
        {
            Action = "player-ban",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},reason={command.Reason},until={(command.Until.HasValue ? command.Until.Value.ToString("O") : "permanent")}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // TODO(Step 5 H5)：DEL PLAYER:{uuid} / PROFILE:{uuid} + 若该玩家为令牌 selectedProfile 则 Lua 吊销——
        // 经 Shared 接口接入，消除跨模块缓存访问（审计 H5）

        return BanPlayerResult.Success();
    }
}
