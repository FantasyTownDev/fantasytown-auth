namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 强制重置密码命令（V5 §7.3 POST /admin/users/{uid}/force-reset-password ★）
/// 管理员不接触明文密码
/// </summary>
public sealed record ForceResetPasswordCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 强制重置密码命令结果
/// </summary>
public sealed record ForceResetPasswordResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static ForceResetPasswordResult Success() => new() { IsSuccess = true };

    public static ForceResetPasswordResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
