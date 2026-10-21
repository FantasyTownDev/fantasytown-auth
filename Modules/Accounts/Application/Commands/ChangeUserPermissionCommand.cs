using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 任命/撤销协管命令（V5 §7.3 POST /admin/users/{uid}/permission ★）
/// </summary>
public sealed record ChangeUserPermissionCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>
    /// 目标新角色：仅允许 NormalPlayer（撤销协管）或 ServerModerator（任命协管），
    /// 提权至 owner 属引导流程（种子/CLI），不走管理操作
    /// </summary>
    public required UserPermission NewPermission { get; init; }
}

/// <summary>
/// 任命/撤销协管命令结果
/// </summary>
public sealed record ChangeUserPermissionResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public UserPermission? OldPermission { get; init; }
    public UserPermission? NewPermission { get; init; }

    public static ChangeUserPermissionResult Success(UserPermission oldPermission, UserPermission newPermission) => new()
    {
        IsSuccess = true,
        OldPermission = oldPermission,
        NewPermission = newPermission
    };

    public static ChangeUserPermissionResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
