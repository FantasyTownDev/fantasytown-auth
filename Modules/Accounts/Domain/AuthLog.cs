// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class AuthLog
{
    public int Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public int? ActorId { get; set; }

    public int SubjectId { get; set; }

    public string? Context { get; set; }

    public string? Ip { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
