// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 强制重置密码命令（V5 §7.3 POST /admin/users/{uid}/force-reset-password ★）
/// 管理员不接触明文密码
/// </summary>
public sealed record ForceResetPasswordCommand
{
    /// <summary>目标用户 UID</summary>
    public required int TargetUid { get; init; }

    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }
}

/// <summary>
/// 强制重置密码命令结果
/// </summary>
public sealed record ForceResetPasswordResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static ForceResetPasswordResult Success() => new() { IsSuccess = true };

    public static ForceResetPasswordResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
