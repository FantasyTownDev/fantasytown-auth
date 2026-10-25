using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Persistence;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家重置皮肤处理器（V5 §7.3）：
/// tid_skin=NULL + 更新 last_modified + 审计 texture-reset。
/// 副作用 DEL PLAYER/PROFILE 随 Step 5 H5 经 Shared 接口接入。
/// </summary>
public sealed class ResetPlayerTextureHandler
{
    private readonly AuthDbContext _db;

    public ResetPlayerTextureHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ResetPlayerTextureResult> HandleAsync(
        ResetPlayerTextureCommand command,
        CancellationToken cancellationToken = default)
    {
        // 统一授权闸门（§7.2 玩家行）
        var gate = await PlayerAuthorization.EvaluateModeratorTierAsync(
            _db, command.ActorUid, command.Pid, "texture-reset", cancellationToken);
        if (gate.Denial != null)
            return ResetPlayerTextureResult.Failure(gate.Denial);

        var player = gate.Player!;
        var hadSkin = player.TidSkin != null;
        player.TidSkin = null;
        player.LastModified = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuthLogs.Add(new AuthLog
        {
            Action = "texture-reset",
            ActorId = command.ActorUid,
            SubjectId = gate.Owner!.Uid,
            Context = $"pid={command.Pid},had_skin={hadSkin.ToString().ToLowerInvariant()}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        // TODO(Step 5 H5)：DEL PLAYER:{uuid} / PROFILE:{uuid}——经 Shared 接口接入（审计 H5）

        return ResetPlayerTextureResult.Success(hadSkin);
    }
}
