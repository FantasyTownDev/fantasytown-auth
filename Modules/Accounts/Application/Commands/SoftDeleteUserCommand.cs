// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 软删除用户命令
/// </summary>
public sealed record SoftDeleteUserCommand
{
    /// <summary>
    /// 目标用户 UID
    /// </summary>
    public required int TargetUid { get; init; }

    /// <summary>
    /// 操作者 UID（审计用）
    /// </summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 软删除用户命令结果
/// </summary>
public sealed record SoftDeleteUserResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ReleasedName { get; init; }

    public static SoftDeleteUserResult Success(string releasedName) => new()
    {
        IsSuccess = true,
        ReleasedName = releasedName
    };

    public static SoftDeleteUserResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
