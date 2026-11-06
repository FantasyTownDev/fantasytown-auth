// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Collections.Concurrent;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 内存限流器（用于单元测试）
/// </summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();
    private readonly int _limit;
    private readonly TimeSpan _window;

    public InMemoryRateLimiter(int limit = 30, TimeSpan? window = null)
    {
        _limit = limit;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public ValueTask<bool> IsAllowedAsync(string key, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var records = _requests.GetOrAdd(key, _ => new List<DateTime>());

        lock (records)
        {
            records.RemoveAll(r => now - r > _window);

            if (records.Count >= _limit)
            {
                return ValueTask.FromResult(false);
            }

            records.Add(now);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (_requests.TryGetValue(key, out var records))
        {
            lock (records)
            {
                records.RemoveAll(r => now - r > _window);
                return ValueTask.FromResult(records.Count);
            }
        }

        return ValueTask.FromResult(0);
    }
}
