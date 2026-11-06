// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// 单元测试用 IPlayerDirectory 替身：
/// 读路径委托真实 PlayerDirectory（InMemory EF 可用），仅 AssignUuidAsync 走变更跟踪——
/// 生产实现的 ExecuteUpdateAsync 不受 InMemory 提供程序支持（并发正确性由 MariaDB 集成测试覆盖）。
/// </summary>
internal sealed class TestPlayerDirectory : IPlayerDirectory
{
    private readonly AuthDbContext _db;
    private readonly PlayerDirectory _inner;

    public TestPlayerDirectory(AuthDbContext db)
    {
        _db = db;
        _inner = new PlayerDirectory(db);
    }

    public ValueTask<PlayerInfo?> FindByUuidAsync(string uuid, CancellationToken cancellationToken = default)
        => _inner.FindByUuidAsync(uuid, cancellationToken);

    public ValueTask<PlayerInfo?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        => _inner.FindByNameAsync(name, cancellationToken);

    public ValueTask<IReadOnlyList<PlayerInfo>> GetProfilesByUserAsync(int uid, CancellationToken cancellationToken = default)
        => _inner.GetProfilesByUserAsync(uid, cancellationToken);

    public async ValueTask<bool> AssignUuidAsync(int pid, string uuid, CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Pid == pid && (p.Uuid == null || p.Uuid == ""), cancellationToken);
        if (player == null)
            return false;

        player.Uuid = uuid;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false; // uuid UNIQUE 并发冲突（单测路径实际不触发）
        }
    }

    public ValueTask<AccountInfo?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _inner.FindAccountByEmailAsync(email, cancellationToken);
}
