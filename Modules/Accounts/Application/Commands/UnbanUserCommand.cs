namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户解封命令（V5 §7.3 POST /admin/users/{uid}/unban）
/// </summary>
public sealed record UnbanUserCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 账户解封命令结果（WasBanned=false 表示本就未封禁的幂等成功）
/// </summary>
public sealed record UnbanUserResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasBanned { get; init; }

    public static UnbanUserResult Success(bool wasBanned) => new()
    {
        IsSuccess = true,
        WasBanned = wasBanned
    };

    public static UnbanUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
