using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 注册命令处理器
/// 使用直接 INSERT + 捕获 DbUpdateException 方式处理并发
/// </summary>
public sealed class RegisterUserHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;

    public RegisterUserHandler(AuthDbContext db, IPasswordService passwordService)
    {
        _db = db;
        _passwordService = passwordService;
    }

    /// <summary>
    /// 处理注册命令
    /// </summary>
    public async Task<RegisterUserResult> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        // 1. 验证用户名格式
        if (!IsValidUsername(command.Username))
        {
            return RegisterUserResult.Failure("用户名格式无效");
        }

        // 2. 验证密码强度
        if (!IsValidPassword(command.Password))
        {
            return RegisterUserResult.Failure("密码强度不足");
        }

        // 3. 验证邮箱格式（如果提供）
        if (!string.IsNullOrEmpty(command.Email) && !IsValidEmail(command.Email))
        {
            return RegisterUserResult.Failure("邮箱格式无效");
        }

        // 4. 哈希密码
        var passwordHash = _passwordService.HashPassword(command.Password);

        // 5. 生成玩家 UUID
        var playerUuid = Guid.NewGuid().ToString("N").ToUpperInvariant();

        // 6. 检查是否为种子邮箱（首次注册时提权为服主）
        var isFirstUser = !await _db.Users.AnyAsync(cancellationToken);
        var isSeedEmail = IsSeedEmail(command.Email);

        // 7. 创建用户
        var user = new User
        {
            Email = command.Email ?? string.Empty,
            Password = passwordHash,
            Ip = command.ClientIp,
            Permission = (isFirstUser && isSeedEmail) ? UserPermission.ServerOwner : UserPermission.NormalPlayer,
            IsBanned = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            Verified = false,
            IsDeleted = false,
            RegisterAt = DateTime.UtcNow
        };

        _db.Users.Add(user);

        try
        {
            // 8. 先保存 User 获取生成的 Uid
            await _db.SaveChangesAsync(cancellationToken);

            // 9. 创建玩家记录（使用已生成的 Uid）
            var player = new Player
            {
                Uid = user.Uid,
                Name = command.Username,
                Uuid = playerUuid,
                IsBanned = false,
                LastModified = DateTime.UtcNow
            };

            _db.Players.Add(player);
            await _db.SaveChangesAsync(cancellationToken);

            // 10. 增加注册 IP 的注册次数（Redis）
            // TODO: 实现 Redis INCR REG_IP:{clientIp}

            return RegisterUserResult.Success(user.Uid, command.Username, Guid.Parse(playerUuid));
        }
        catch (DbUpdateException ex)
        {
            // 捕获唯一性约束冲突（MySQL 1062）
            // 检查是用户名还是邮箱冲突
            if (IsUniqueConstraintViolation(ex))
            {
                // 回滚事务
                if (_db.ChangeTracker.HasChanges())
                {
                    foreach (var entry in _db.ChangeTracker.Entries())
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                // 判断是哪个字段冲突
                if (await _db.Players.AnyAsync(p => p.Name == command.Username, cancellationToken))
                {
                    return RegisterUserResult.Failure("用户名已被占用");
                }

                if (!string.IsNullOrEmpty(command.Email) && await _db.Users.AnyAsync(u => u.Email == command.Email, cancellationToken))
                {
                    return RegisterUserResult.Failure("邮箱已被注册");
                }

                return RegisterUserResult.Failure("注册失败，请稍后重试");
            }

            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // MySQL 1062 = Duplicate entry for unique key
        // MariaDB 使用相同的错误码
        var innerException = ex.InnerException;
        if (innerException != null)
        {
            var message = innerException.Message;
            if (message.Contains("Duplicate entry") || message.Contains("1062"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsValidUsername(string username)
    {
        // 用户名规则：3-16个字符，字母数字下划线
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (username.Length < 3 || username.Length > 16) return false;
        return username.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static bool IsValidPassword(string password)
    {
        // 密码规则：至少8个字符
        if (string.IsNullOrWhiteSpace(password)) return false;
        return password.Length >= 8;
    }

    private static bool IsValidEmail(string email)
    {
        // 简单邮箱验证：必须包含 @ 且 @ 前后都有内容
        if (string.IsNullOrWhiteSpace(email)) return false;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 && atIndex < email.Length - 1 && email.Contains('.');
    }

    /// <summary>
    /// 检查是否为种子邮箱
    /// </summary>
    private static bool IsSeedEmail(string? email)
    {
        // TODO: 从配置读取种子邮箱列表
        // 目前默认 owner@fantasytown.com 为种子邮箱
        return email?.Equals("owner@fantasytown.com", StringComparison.OrdinalIgnoreCase) == true;
    }
}
