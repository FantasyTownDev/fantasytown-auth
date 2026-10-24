namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家改名命令（V5 §7.3 POST /admin/players/{pid}/rename，owner 策略；uuid 不变）
/// </summary>
public sealed record RenamePlayerCommand
{
    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>目标玩家 PID</summary>
    public required int Pid { get; init; }

    /// <summary>新角色名</summary>
    public required string NewName { get; init; }
}

/// <summary>
/// 玩家改名命令结果（OldName 供审计与缓存失效）
/// </summary>
public sealed record RenamePlayerResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OldName { get; init; }

    public static RenamePlayerResult Success(string oldName) => new()
    {
        IsSuccess = true,
        OldName = oldName
    };

    public static RenamePlayerResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
