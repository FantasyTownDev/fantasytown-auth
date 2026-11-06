// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class User
{
    public int Uid { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string? Ip { get; set; }

    public UserPermission Permission { get; set; } = UserPermission.NormalPlayer;

    public bool IsBanned { get; set; }

    public DateTime? BannedUntil { get; set; }

    public string? BannedReason { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public bool Verified { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime RegisterAt { get; set; } = DateTime.UtcNow;

    public Player? Player { get; set; }
}
