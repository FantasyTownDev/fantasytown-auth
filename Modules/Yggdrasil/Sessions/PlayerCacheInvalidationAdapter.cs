// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// IPlayerCacheInvalidation 的 Yggdrasil 实现（H5）：封装 PLAYER:{uuid} 删除，
/// 供 Accounts 玩家管理命令经 Shared 接口触发（V5 §7.3 封禁/解封/改名/重置皮肤副作用）。
/// </summary>
public sealed class PlayerCacheInvalidationAdapter : IPlayerCacheInvalidation
{
    private readonly IPlayerCache _playerCache;

    public PlayerCacheInvalidationAdapter(IPlayerCache playerCache) => _playerCache = playerCache;

    public ValueTask InvalidatePlayerAsync(string uuid, CancellationToken cancellationToken = default)
        => _playerCache.RemoveAsync(uuid, cancellationToken);
}
