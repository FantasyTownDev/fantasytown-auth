namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户封禁命令（V5 §7.3 POST /admin/users/{uid}/ban）
/// 封禁 users.is_banned（含 until/reason），区别于玩家封禁状态机（player_bans，§5.3）
/// </summary>
public sealed record BanUserCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>封禁理由（可空）</summary>
    public string? Reason { get; init; }

    /// <summary>封禁截止时间（null = 永久）</summary>
    public DateTime? Until { get; init; }
}

/// <summary>
/// 账户封禁命令结果
/// </summary>
public sealed record BanUserResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static BanUserResult Success() => new() { IsSuccess = true };

    public static BanUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
