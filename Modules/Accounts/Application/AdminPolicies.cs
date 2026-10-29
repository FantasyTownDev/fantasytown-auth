// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Accounts.Application;

/// <summary>
/// 鉴权三策略（V5 §5.2 冻结规格；实施指导 §2.3 第 3 节装配）。
/// 谓词只信 DataProtection 保护的 uid claim，角色/封禁一律读 PERM 快照——
/// 撤权/封禁随写路径 DEL PERM 对已签发 Cookie 即时生效（防错 #1/#2）。
/// </summary>
public static class AdminPolicies
{
    /// <summary>协管及以上策略（/admin 文件夹兜底与只读页）</summary>
    public const string Moderator = "moderator";

    /// <summary>服主策略（用户域管理与收紧页）</summary>
    public const string Owner = "owner";

    /// <summary>平台级策略——PlatformAdmin 预留前以服主能力视为持有（V5 §5.2 说明）</summary>
    public const string Admin = "admin";

    /// <summary>
    /// 策略纯谓词（单元测试锚点）：
    /// 快照缺失（null 防御残留）按封禁等效拒绝；角色白名单精确匹配 + 封禁时间谓词。
    /// owner 层白名单 = ServerOwner ∨ PlatformAdmin（与 §7.2 矩阵 owner 行、
    /// AdminAuthorization 闸门一致；平台管理位预留启用后天然通过）。
    /// </summary>
    public static bool IsSatisfied(PermissionSnapshot? snapshot, bool ownerTier, DateTime nowUtc) =>
        snapshot != null
        && (ownerTier
            ? PermissionRules.IsOwner(snapshot.Permission) || PermissionRules.IsPlatformAdmin(snapshot.Permission)
            : PermissionRules.IsModerator(snapshot.Permission))
        && !BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, nowUtc);

    /// <summary>
    /// 策略谓词入口：uid claim → PERM 快照 → 纯谓词。
    /// Resource 无服务提供者（如测试旁路）时 fail-closed 返回 false。
    /// </summary>
    public static async Task<bool> EvaluateAsync(AuthorizationHandlerContext context, bool ownerTier)
    {
        var uid = context.User.GetUid();
        if (uid is null)
            return false;

        var services = context.Resource switch
        {
            HttpContext http => http.RequestServices,
            AuthorizationFilterContext mvc => mvc.HttpContext.RequestServices,
            _ => null
        };
        if (services is null)
            return false;

        var snapshot = await services.GetRequiredService<IPermissionSnapshot>().GetAsync(uid.Value);
        var nowUtc = services.GetRequiredService<IClock>().UtcNow.UtcDateTime;
        return IsSatisfied(snapshot, ownerTier, nowUtc);
    }
}
