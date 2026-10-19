using System.Security.Cryptography;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 密码重置处理器
/// </summary>
public sealed class PasswordResetHandler
{
    private const string TokenPurpose = "FantasyTown.Auth.PasswordReset";
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(1);

    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly ITimeLimitedDataProtector _tokenProtector;
    private readonly TimeSpan _tokenLifetime;

    public PasswordResetHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IDataProtectionProvider dataProtectionProvider,
        TimeSpan? resetTokenLifetime = null)
    {
        _db = db;
        _passwordService = passwordService;
        _tokenProtector = dataProtectionProvider.CreateProtector(TokenPurpose).ToTimeLimitedDataProtector();
        _tokenLifetime = resetTokenLifetime ?? DefaultTokenLifetime;
    }

    /// <summary>
    /// 请求密码重置
    /// </summary>
    public async Task<RequestPasswordResetResult> HandleRequestAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. 查找用户（全局查询过滤器自动排除软删用户，V5 §4.5）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == command.Email, cancellationToken);

        if (user == null)
        {
            // 安全考虑：即使用户不存在也返回成功（统一话术防枚举）
            return RequestPasswordResetResult.Success();
        }

        // 2. 生成限时重置令牌（V5 §4.5：DataProtection 载荷 = 用途|uid|securityStamp，
        //    密文不暴露裸 stamp，独立于 Cookie 会话凭据，审计 C2）
        var payload = $"reset|{user.Uid}|{user.SecurityStamp}";
        var resetToken = _tokenProtector.Protect(payload, _tokenLifetime);

        // 3. TODO: 发送重置邮件（MailService 未实现，令牌随邮件链接下发）

        return RequestPasswordResetResult.Success(resetToken);
    }

    /// <summary>
    /// 重置密码
    /// </summary>
    public async Task<ResetPasswordResult> HandleResetAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. 验证新密码强度
        if (command.NewPassword.Length < 8)
        {
            return ResetPasswordResult.Failure("密码强度不足");
        }

        // 2. 解护令牌（V5 §4.5：校验时效与用途；无效/过期/篡改一律拒绝）
        if (!TryUnprotectToken(command.Token, out var uid, out var expectedStamp))
        {
            return ResetPasswordResult.Failure("无效的重置令牌");
        }

        // 3. 查找用户并比对 securityStamp（软删用户经查询过滤器拒绝）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Uid == uid, cancellationToken);

        if (user == null || user.SecurityStamp != expectedStamp)
        {
            return ResetPasswordResult.Failure("无效的重置令牌");
        }

        // 4. 更新密码并轮换 securityStamp（旧链接一次性失效，审计 C2）
        var newPasswordHash = _passwordService.HashPassword(command.NewPassword);
        user.Password = newPasswordHash;
        user.SecurityStamp = Guid.NewGuid().ToString();

        // 5. 保存更改
        await _db.SaveChangesAsync(cancellationToken);

        return ResetPasswordResult.Success();
    }

    /// <summary>
    /// 解护重置令牌并解析载荷（用途|uid|securityStamp）
    /// </summary>
    private bool TryUnprotectToken(string token, out int uid, out string securityStamp)
    {
        uid = 0;
        securityStamp = string.Empty;

        string payload;
        try
        {
            payload = _tokenProtector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            // 令牌无效、已过期或用途不匹配
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3 || parts[0] != "reset" || !int.TryParse(parts[1], out uid))
        {
            return false;
        }

        securityStamp = parts[2];
        return true;
    }
}
