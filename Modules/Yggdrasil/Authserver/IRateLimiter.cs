// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 限流器接口
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// 检查是否允许请求。允许时自动计数。
    /// </summary>
    ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前窗口内的请求计数。
    /// </summary>
    ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default);
}
