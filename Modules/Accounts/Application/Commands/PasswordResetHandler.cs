using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 密码重置处理器
/// </summary>
public sealed class PasswordResetHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;

    public PasswordResetHandler(AuthDbContext db, IPasswordService passwordService)
    {
        _db = db;
        _passwordService = passwordService;
    }

    /// <summary>
    /// 请求密码重置
    /// </summary>
    public async Task<RequestPasswordResetResult> HandleRequestAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. 查找用户
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == command.Email, cancellationToken);

        if (user == null)
        {
            // 安全考虑：即使用户不存在也返回成功
            return RequestPasswordResetResult.Success();
        }

        // 2. 生成重置令牌（使用 security_stamp 作为令牌）
        var resetToken = user.SecurityStamp;

        // 3. TODO: 发送重置邮件（需要实现邮件服务）

        return RequestPasswordResetResult.Success();
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

        // 2. 查找用户（通过 security_stamp 匹配令牌）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.SecurityStamp == command.Token, cancellationToken);

        if (user == null)
        {
            return ResetPasswordResult.Failure("无效的重置令牌");
        }

        // 3. 哈希新密码
        var newPasswordHash = _passwordService.HashPassword(command.NewPassword);

        // 4. 更新密码
        user.Password = newPasswordHash;

        // 5. 轮换 security_stamp（使旧令牌失效）
        user.SecurityStamp = Guid.NewGuid().ToString();

        // 6. 保存更改
        await _db.SaveChangesAsync(cancellationToken);

        return ResetPasswordResult.Success();
    }
}
