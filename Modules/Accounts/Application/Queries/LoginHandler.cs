// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Application.Queries;

/// <summary>
/// 登录查询处理器
/// </summary>
public sealed class LoginHandler
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionSnapshot _permissionSnapshot;
    private readonly ILockoutService _lockoutService;

    public LoginHandler(
        AuthDbContext db,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot,
        ILockoutService? lockoutService = null)
    {
        _db = db;
        _passwordService = passwordService;
        _permissionSnapshot = permissionSnapshot;
        _lockoutService = lockoutService ?? new InMemoryLockoutService();
    }

    /// <summary>
    /// 处理登录查询
    /// </summary>
    public async Task<LoginResult> HandleAsync(LoginQuery query, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;

        // 1. 查找用户（支持邮箱或玩家名登录）
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == query.Username, cancellationToken);

        if (user == null)
        {
            user = await _db.Users
                .Include(u => u.Player)
                .FirstOrDefaultAsync(u => u.Player != null && u.Player.Name == query.Username, cancellationToken);
        }

        if (user == null)
        {
            // 未知账户：仅记 FAIL_ACCT 审计维度（V5 §6.6，不触发锁定），等耗时操作防枚举
            await _lockoutService.RecordUnknownAccountFailureAsync(query.Username, cancellationToken);
            await _passwordService.VerifyPasswordAsync(query.Password, string.Empty, cancellationToken);
            return LoginResult.Failure("用户名或密码错误");
        }

        // 2. 检查是否被软删除
        // 防枚举（审计 H2）：与未知账号统一报错话术，差异仅留审计（IAuditChannel P4 接入）
        if (user.IsDeleted)
        {
            return LoginResult.Failure("用户名或密码错误");
        }

        // 3. 封禁判定暂存（防错 #17：判定前置；V5 §6.5/§4.4 评审修订——验密通过后才揭示，
        //    密码错误与正常账户同文案同计时，防枚举）
        var banEffective = BanRules.IsBanEffective(user.IsBanned, user.BannedUntil, nowUtc);

        // 4. LOCKOUT 检查（LOCKOUT:{uid}，Web 与 Ygg authenticate 共享键空间）——
        //    前置拒绝省 CPU；§8：锁定状态不外显，与凭据错误同文案
        if (await _lockoutService.IsLockedOutAsync(user.Uid, cancellationToken))
        {
            return LoginResult.Failure("用户名或密码错误");
        }

        // 5. 验证密码
        var passwordValid = await _passwordService.VerifyPasswordAsync(query.Password, user.Password, cancellationToken);

        if (!passwordValid)
        {
            // 记录失败：FAIL_EP:{email}:{ip}（同 IP 窗口 ≥5 触发 LOCKOUT）+ FAIL_ACCT:{email}（审计）
            await _lockoutService.RecordFailureAsync(user.Uid, user.Email, query.ClientIp, cancellationToken);

            return LoginResult.Failure("用户名或密码错误");
        }

        // 6. 密码正确，清除 LOCKOUT:{uid} 与 FAIL_EP:{email}:{ip}
        await _lockoutService.ResetAsync(user.Uid, user.Email, query.ClientIp, cancellationToken);

        // 6.5 封禁揭示（仅向密码持有者披露；须在 7 之前——被封账户不得覆盖 PERM 快照；
        //     过期封禁 banEffective=false 已落入正常登录并在 8 清除字段）
        if (banEffective)
        {
            var remainingSeconds = user.BannedUntil.HasValue
                ? BanRules.GetBanRemainingSeconds(user.BannedUntil, nowUtc) ?? 0
                : -1; // -1 = 永久封禁

            return LoginResult.Banned(remainingSeconds, BanRules.RevealMessage(user.BannedUntil, user.BannedReason));
        }

        // 7. 更新权限快照
        await _permissionSnapshot.SetAsync(user.Uid, new PermissionSnapshot
        {
            Permission = user.Permission,
            IsBanned = false,
            CreatedAt = nowUtc
        }, cancellationToken);

        // 8. 如果之前被封禁但已过期，清除封禁状态
        if (user.IsBanned && user.BannedUntil.HasValue && user.BannedUntil.Value <= nowUtc)
        {
            user.IsBanned = false;
            user.BannedUntil = null;
            user.BannedReason = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var displayName = user.Player?.Name ?? user.Email;
        return LoginResult.Success(user.Uid, displayName, user.Permission);
    }
}
