namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 软删除用户命令
/// </summary>
public sealed record SoftDeleteUserCommand
{
    /// <summary>
    /// 目标用户 UID
    /// </summary>
    public required int TargetUid { get; init; }

    /// <summary>
    /// 操作者 UID（审计用）
    /// </summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 软删除用户命令结果
/// </summary>
public sealed record SoftDeleteUserResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ReleasedName { get; init; }

    public static SoftDeleteUserResult Success(string releasedName) => new()
    {
        IsSuccess = true,
        ReleasedName = releasedName
    };

    public static SoftDeleteUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
