// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 邮箱格式校验（注册与管理域邮箱改绑共用同一规则）
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// 简单邮箱验证：必须包含 @ 且 @ 前后都有内容；长度封顶 100（M2：对齐
    /// users.email VARCHAR(100) 列限，超长输入前置拒绝，不让 1406 冒泡成 500）
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Length > 100) return false;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 && atIndex < email.Length - 1 && email.Contains('.');
    }
}
