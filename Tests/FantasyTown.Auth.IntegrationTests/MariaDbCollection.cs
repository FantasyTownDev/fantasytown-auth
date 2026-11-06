// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 所有依赖本地 MariaDB 共享库的集成测试归入同一集合串行执行：
/// 并行类间会竞争库全局状态（如种子提权的"库中无任何 ServerOwner"前置、UNIQUE 占位），
/// 跨类并行曾导致 ConcurrentSeedEmailRegistration 与玩家管理测试的提权判定竞争失败。
/// </summary>
[CollectionDefinition("MariaDB")]
public class MariaDbCollection
{
}
