namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 管理员清除账户锁定命令（V5 §7.3 POST /admin/lockouts/{uid}/clear，moderator 策略）
/// </summary>
public sealed record ClearUserLockoutCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 清除账户锁定命令结果（WasLocked 反映清除前是否处于锁定）
/// </summary>
public sealed record ClearUserLockoutResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasLocked { get; init; }

    public static ClearUserLockoutResult Success(bool wasLocked) => new()
    {
        IsSuccess = true,
        WasLocked = wasLocked
    };

    public static ClearUserLockoutResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
