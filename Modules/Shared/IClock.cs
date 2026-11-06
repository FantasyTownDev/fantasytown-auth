// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 时钟抽象，支持确定性测试。
/// 注入到需要当前 UTC 时间的位置（如 IsBanEffective 检查）。
/// </summary>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 日期和时间。
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 使用系统时钟的默认实现。
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
