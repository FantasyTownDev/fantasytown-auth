// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class PlayerBan
{
    public int BanId { get; set; }

    public int Pid { get; set; }

    public DateTime BannedAt { get; set; } = DateTime.UtcNow;

    public int BannedBy { get; set; }

    public string BannedReason { get; set; } = string.Empty;

    public DateTime? BannedUntil { get; set; }

    public bool IsActive { get; set; }

    public Player Player { get; set; } = null!;
}
