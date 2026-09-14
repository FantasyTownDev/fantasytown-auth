using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 用户注册命令
/// </summary>
public sealed record RegisterUserCommand
{
    /// <summary>
    /// 用户名
    /// </summary>
    public required string Username { get; init; }
    
    /// <summary>
    /// 密码
    /// </summary>
    public required string Password { get; init; }
    
    /// <summary>
    /// 邮箱（可选，用于找回密码）
    /// </summary>
    public string? Email { get; init; }
    
    /// <summary>
    /// 客户端 IP
    /// </summary>
    public required string ClientIp { get; init; }
}

/// <summary>
/// 注册命令结果
/// </summary>
public sealed record RegisterUserResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// 用户 ID（成功时返回）
    /// </summary>
    public int? UserId { get; init; }
    
    /// <summary>
    /// 用户名（成功时返回）
    /// </summary>
    public string? Username { get; init; }
    
    /// <summary>
    /// 创建的玩家 UUID（成功时返回）
    /// </summary>
    public Guid? PlayerUuid { get; init; }
    
    public static RegisterUserResult Success(int userId, string username, Guid playerUuid) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Username = username,
        PlayerUuid = playerUuid
    };
    
    public static RegisterUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
