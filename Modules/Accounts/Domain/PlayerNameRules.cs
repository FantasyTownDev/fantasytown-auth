// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 角色名规范（现行规则与注册路径一致：3–16 位字母/数字/下划线）。
/// SiteOptions.PlayerNameRule/PlayerNameLength 的 official/cjk/utf8 分级规则引擎
/// 尚未实现（配置项预留），实现后此处为唯一收敛点。
/// </summary>
public static class PlayerNameRules
{
    public static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length < 3 || name.Length > 16) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
