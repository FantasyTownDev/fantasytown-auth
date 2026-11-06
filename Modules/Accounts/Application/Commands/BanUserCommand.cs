// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 账户封禁命令（V5 §7.3 POST /admin/users/{uid}/ban）
/// 封禁 users.is_banned（含 until/reason），区别于玩家封禁状态机（player_bans，§5.3）
/// </summary>
public sealed record BanUserCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>封禁理由（可空）</summary>
    public string? Reason { get; init; }

    /// <summary>封禁截止时间（null = 永久）</summary>
    public DateTime? Until { get; init; }
}

/// <summary>
/// 账户封禁命令结果
/// </summary>
public sealed record BanUserResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static BanUserResult Success() => new() { IsSuccess = true };

    public static BanUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
