namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 邮箱改绑命令（V5 §7.3 POST /admin/users/{uid}/email）
/// </summary>
public sealed record ChangeUserEmailCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>新邮箱</summary>
    public required string NewEmail { get; init; }
}

/// <summary>
/// 邮箱改绑命令结果（Success 携带旧邮箱供审计/回显核对）
/// </summary>
public sealed record ChangeUserEmailResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OldEmail { get; init; }

    public static ChangeUserEmailResult Success(string oldEmail) => new()
    {
        IsSuccess = true,
        OldEmail = oldEmail
    };

    public static ChangeUserEmailResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
