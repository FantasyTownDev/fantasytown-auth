namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家解封命令（V5 §7.3 POST /admin/players/{pid}/unban，moderator 策略）
/// </summary>
public sealed record UnbanPlayerCommand
{
    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>目标玩家 PID</summary>
    public required int Pid { get; init; }
}

/// <summary>
/// 玩家解封命令结果：0 行按幂等成功处理（防错 #7），WasBanned 反映清除前是否处于封禁；
/// PriorBanCount 供"再犯提醒"展示（§7.3 unban 行）
/// </summary>
public sealed record UnbanPlayerResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasBanned { get; init; }
    public int PriorBanCount { get; init; }

    public static UnbanPlayerResult Success(bool wasBanned, int priorBanCount) => new()
    {
        IsSuccess = true,
        WasBanned = wasBanned,
        PriorBanCount = priorBanCount
    };

    public static UnbanPlayerResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
