// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// 种子服主提权独占窗（审计 C1）：
/// 以会话级咨询锁串行化"是否存在 ServerOwner"判定与注册落库，
/// 保证并发种子邮箱注册恰好一个获得 ServerOwner（V5 §4 引导语义"仅生效一次"）
/// </summary>
public interface IOwnerClaimLock
{
    /// <summary>
    /// 在独占窗内执行回调；acquired=false 表示未获锁（另一注册正在引导中），
    /// 调用方必须放弃提权（按普通玩家落库），绝不允许未持锁判定提权
    /// </summary>
    Task<T> RunExclusiveAsync<T>(Func<bool, Task<T>> action, CancellationToken cancellationToken = default);
}
