namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 请求密码重置命令
/// </summary>
public sealed record RequestPasswordResetCommand
{
    /// <summary>
    /// 邮箱
    /// </summary>
    public required string Email { get; init; }
}

/// <summary>
/// 请求密码重置结果
/// </summary>
public sealed record RequestPasswordResetResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    public static RequestPasswordResetResult Success() => new()
    {
        IsSuccess = true
    };
    
    public static RequestPasswordResetResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}

/// <summary>
/// 重置密码命令
/// </summary>
public sealed record ResetPasswordCommand
{
    /// <summary>
    /// 重置令牌
    /// </summary>
    public required string Token { get; init; }
    
    /// <summary>
    /// 新密码
    /// </summary>
    public required string NewPassword { get; init; }
}

/// <summary>
/// 重置密码结果
/// </summary>
public sealed record ResetPasswordResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    public static ResetPasswordResult Success() => new()
    {
        IsSuccess = true
    };
    
    public static ResetPasswordResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
