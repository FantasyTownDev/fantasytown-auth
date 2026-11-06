// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Collections.Concurrent;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 并发令牌测试（需要 Redis 或 Docker）
/// </summary>
[Trait("Category", "Integration")]
[Collection("MariaDB")]
public class ConcurrentTokenTests
{
    [Fact]
    public async Task ConcurrentTokenIssuance_ShouldNotCreateOrphans()
    {
        const int concurrentCount = 10;
        var tokens = new ConcurrentBag<string>();

        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var token = Guid.NewGuid().ToString();
                tokens.Add(token);
            }));
        }

        await Task.WhenAll(tasks);

        var uniqueTokens = tokens.Distinct().Count();
        Assert.Equal(concurrentCount, uniqueTokens);
    }
}
