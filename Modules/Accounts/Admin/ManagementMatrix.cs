// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// 管理管线越权矩阵档位（V5 §7.2）：
/// 各档位的白名单/CanManage/NotSelf 判定收敛于 Application 层闸门（唯一实现点），
/// 管线仅按档位分派。Step 4 玩家域接入时在此扩展档位并补对应闸门。
/// </summary>
public enum ManagementMatrix
{
    /// <summary>owner 级用户域操作（任命/撤销协管、封禁/解封、强制重置、邮箱改绑、软删）</summary>
    OwnerUser,

    /// <summary>解除 LOCKOUT（moderator 策略；自我解锁放行，V5 评审 N1）</summary>
    ClearLockout,

    /// <summary>玩家域 moderator 操作（封禁/解封/重置皮肤；约束在属主：CanManage + NotSelf，§7.2/§7.5）</summary>
    PlayerModerator,

    /// <summary>玩家域 owner 操作（改名；约束同上 + owner 白名单，§7.2）</summary>
    PlayerOwner
}
