using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.Modules.Accounts.Application.Queries;

/// <summary>
/// 登录查询
/// </summary>
public sealed record LoginQuery
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
    /// 客户端 IP
    /// </summary>
    public required string ClientIp { get; init; }
    
    /// <summary>
    /// 验证码（渐进式，后续实现）
    /// </summary>
    public string? CaptchaToken { get; init; }
}

/// <summary>
/// 登录查询结果
/// </summary>
public sealed record LoginResult
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
    /// 权限（成功时返回）
    /// </summary>
    public UserPermission? Permission { get; init; }
    
    /// <summary>
    /// 是否被封禁
    /// </summary>
    public bool IsBanned { get; init; }
    
    /// <summary>
    /// 封禁剩余时间（秒）
    /// </summary>
    public double? BanRemainingSeconds { get; init; }
    
    /// <summary>
    /// 锁定剩余时间（秒）
    /// </summary>
    public double? LockRemainingSeconds { get; init; }
    
    /// <summary>
    /// 需要验证码
    /// </summary>
    public bool RequiresCaptcha { get; init; }
    
    public static LoginResult Success(int userId, string username, UserPermission permission) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Username = username,
        Permission = permission
    };
    
    public static LoginResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
    
    public static LoginResult Banned(double remainingSeconds, string? reason = null) => new()
    {
        IsSuccess = false,
        IsBanned = true,
        BanRemainingSeconds = remainingSeconds,
        ErrorMessage = reason ?? "账户已被封禁"
    };
    
    public static LoginResult Locked(double remainingSeconds) => new()
    {
        IsSuccess = false,
        LockRemainingSeconds = remainingSeconds,
        ErrorMessage = $"账户已锁定，请 {remainingSeconds:F0} 秒后重试"
    };
    
    public static LoginResult RequiresCaptchaLogin() => new()
    {
        IsSuccess = false,
        RequiresCaptcha = true,
        ErrorMessage = "需要验证码"
    };
}
