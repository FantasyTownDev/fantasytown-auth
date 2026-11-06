// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 认证并发许可限流器
/// 全局并发限制 ≈ 2×CPU 核心数，QueueLimit=0，超限 403
/// </summary>
public sealed class AuthConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _queueLimit;

    public AuthConcurrencyLimiter(int maxConcurrency = 0, int queueLimit = 0)
    {
        if (maxConcurrency <= 0)
        {
            maxConcurrency = Environment.ProcessorCount * 2;
        }

        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _queueLimit = queueLimit;
        MaxConcurrency = maxConcurrency;
    }

    public int MaxConcurrency { get; }
    public int CurrentCount => _semaphore.CurrentCount;

    public bool TryAcquire()
    {
        if (_queueLimit == 0 && _semaphore.CurrentCount == 0)
        {
            return false;
        }

        return _semaphore.Wait(0);
    }

    public void Release()
    {
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
