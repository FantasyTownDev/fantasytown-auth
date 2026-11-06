// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 票据服务接口 — 管理 join/hasJoined 一次性票据
/// Redis 键：SERVER:{serverId} → playerUuid, TTL=60s
/// </summary>
public interface ITicketService
{
    /// <summary>
    /// 存储票据（join 时调用）
    /// </summary>
    ValueTask SetAsync(string serverId, string playerUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证并删除票据（hasJoined 时调用，一次性）
    /// </summary>
    ValueTask<string?> ConsumeAsync(string serverId, CancellationToken cancellationToken = default);
}
