namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 密码服务接口
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// 哈希密码
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// 验证密码
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);

    /// <summary>
    /// 验证密码（带耗时，防枚举）
    /// </summary>
    Task<bool> VerifyPasswordAsync(string password, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否需要重新哈希（密码哈希过期）
    /// </summary>
    bool NeedsRehash(string passwordHash);
}
