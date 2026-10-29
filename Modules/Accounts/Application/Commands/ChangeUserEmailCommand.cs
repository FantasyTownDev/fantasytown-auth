// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 邮箱改绑命令（V5 §7.3 POST /admin/users/{uid}/email）
/// </summary>
public sealed record ChangeUserEmailCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>新邮箱</summary>
    public required string NewEmail { get; init; }
}

/// <summary>
/// 邮箱改绑命令结果（Success 携带旧邮箱供审计/回显核对）
/// </summary>
public sealed record ChangeUserEmailResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OldEmail { get; init; }

    public static ChangeUserEmailResult Success(string oldEmail) => new()
    {
        IsSuccess = true,
        OldEmail = oldEmail
    };

    public static ChangeUserEmailResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
