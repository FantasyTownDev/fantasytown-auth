// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
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
    private readonly IRegistrationQuotaService _registrationQuota;
    private readonly SiteOptions _siteOptions;
    private readonly IOwnerClaimLock _ownerClaimLock;

    public RegisterUserHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IRegistrationQuotaService registrationQuota,
        SiteOptions siteOptions,
        IOwnerClaimLock ownerClaimLock)
    {
        _db = db;
        _passwordService = passwordService;
        _registrationQuota = registrationQuota;
        _siteOptions = siteOptions;
        _ownerClaimLock = ownerClaimLock;
    }

    /// <summary>
    /// 处理注册命令
    /// </summary>
    public async Task<RegisterUserResult> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        // 1. 验证用户名格式
        if (!PlayerNameRules.IsValid(command.Username))
        {
            return RegisterUserResult.Failure("用户名格式无效");
        }

        // 2. 验证密码强度
        if (!IsValidPassword(command.Password))
        {
            return RegisterUserResult.Failure("密码强度不足");
        }

        // 3. 验证邮箱格式（如果提供）
        if (!string.IsNullOrEmpty(command.Email) && !EmailAddress.IsValid(command.Email))
        {
            return RegisterUserResult.Failure("邮箱格式无效");
        }

        // 4. 注册配额粗检（REG_IP:{ip} 只读 GET；失败请求不消耗配额，V5 §4.1 软配额）
        if (!await _registrationQuota.CanRegisterAsync(command.ClientIp, cancellationToken))
        {
            return RegisterUserResult.Failure("该 IP 注册过于频繁，请稍后再试");
        }

        // 5. 哈希密码
        var passwordHash = _passwordService.HashPassword(command.Password);

        // 6. 生成玩家 UUID
        var playerUuid = Guid.NewGuid().ToString("N").ToUpperInvariant();

        // 7. 种子邮箱判定（V5 §4 引导：配置 SeedOwnerEmail，空 = 禁用种子）
        var isSeedEmail = IsSeedEmail(command.Email);

        // 审计 C1：种子提权判定与注册落库必须在同一独占窗内（MariaDB 咨询锁），
        // 否则并发注册在竞享窗口内可能双双通过"无服主"判定
        if (isSeedEmail)
        {
            return await _ownerClaimLock.RunExclusiveAsync(
                acquired => RegisterCoreAsync(command, passwordHash, playerUuid, allowSeedGrant: acquired, cancellationToken),
                cancellationToken);
        }

        return await RegisterCoreAsync(command, passwordHash, playerUuid, allowSeedGrant: false, cancellationToken);
    }

    private async Task<RegisterUserResult> RegisterCoreAsync(
        RegisterUserCommand command,
        string passwordHash,
        string playerUuid,
        bool allowSeedGrant,
        CancellationToken cancellationToken)
    {
        // V5 §4 引导语义（审计 C1 重构）：仅当库中不存在任何 ServerOwner（含软删，
        // "仅生效一次"防止种子配置残留导致后续同名邮箱再被提权）且持有独占锁时提权；
        // 判定贴近落库点
        var grantOwner = allowSeedGrant
            && !await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Permission == UserPermission.ServerOwner, cancellationToken);

        // 创建用户与玩家（导航属性关联；EF 按外键依赖拓扑排序，先 User 后 Player）
        var user = new User
        {
            Email = command.Email ?? string.Empty,
            Password = passwordHash,
            Ip = command.ClientIp,
            Permission = grantOwner ? UserPermission.ServerOwner : UserPermission.NormalPlayer,
            IsBanned = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            Verified = false,
            IsDeleted = false,
            RegisterAt = DateTime.UtcNow
        };

        var player = new Player
        {
            Name = command.Username,
            Uuid = playerUuid,
            IsBanned = false,
            LastModified = DateTime.UtcNow
        };

        user.Player = player;
        _db.Users.Add(user);

        try
        {
            // 单次保存：User+Player 同一事务原子落库（审计 M4/T3-013，杜绝孤儿 User 占用邮箱）
            await _db.SaveChangesAsync(cancellationToken);

            // 种子提权审计：actor=NULL（系统引导行为），context=bootstrap=seed（V5 §4）
            if (grantOwner)
            {
                _db.AuthLogs.Add(new AuthLog
                {
                    Action = "permission-change",
                    ActorId = null,
                    SubjectId = user.Uid,
                    Context = "bootstrap=seed",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            // 注册成功后计数（REG_IP:{ip}，仅成功消耗配额；超发告警由配额服务写入）
            await _registrationQuota.RecordRegistrationAsync(command.ClientIp, cancellationToken);

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

    private static bool IsValidPassword(string password)
    {
        // 密码规则：至少8个字符
        if (string.IsNullOrWhiteSpace(password)) return false;
        return password.Length >= 8;
    }

    /// <summary>
    /// 种子邮箱判定（V5 §4：种子值来自配置 SeedOwnerEmail；空 = 禁用种子）
    /// </summary>
    private bool IsSeedEmail(string? email) =>
        !string.IsNullOrEmpty(_siteOptions.SeedOwnerEmail)
        && email?.Equals(_siteOptions.SeedOwnerEmail, StringComparison.OrdinalIgnoreCase) == true;
}
