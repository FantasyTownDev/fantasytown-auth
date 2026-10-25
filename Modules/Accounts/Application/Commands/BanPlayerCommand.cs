namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家封禁命令（V5 §7.3 POST /admin/players/{pid}/ban，moderator 策略）
/// </summary>
public sealed record BanPlayerCommand
{
    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>目标玩家 PID</summary>
    public required int Pid { get; init; }

    /// <summary>封禁理由（player_bans.banned_reason VARCHAR(255)）</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>封禁截止时间（null = 永久）</summary>
    public DateTime? Until { get; init; }
}

/// <summary>
/// 玩家封禁命令结果：409 冲突携带当前生效封禁信息（V5 §5.3 条件更新闸门）
/// </summary>
public sealed record BanPlayerResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? CurrentBannedUntil { get; init; }
    public string? CurrentBannedReason { get; init; }

    public static BanPlayerResult Success() => new() { IsSuccess = true };

    public static BanPlayerResult Conflict(DateTime? bannedUntil, string bannedReason) => new()
    {
        IsSuccess = false,
        ErrorMessage = "该玩家已处于封禁生效期",
        CurrentBannedUntil = bannedUntil,
        CurrentBannedReason = bannedReason
    };

    public static BanPlayerResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
