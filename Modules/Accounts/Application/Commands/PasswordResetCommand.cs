// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 请求密码重置命令
/// </summary>
public sealed record RequestPasswordResetCommand
{
    /// <summary>
    /// 邮箱
    /// </summary>
    public required string Email { get; init; }
}

/// <summary>
/// 请求密码重置结果
/// </summary>
public sealed record RequestPasswordResetResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 重置令牌（仅成功且邮箱存在时下发，随邮件链接传递）
    /// </summary>
    public string? ResetToken { get; init; }

    public static RequestPasswordResetResult Success(string? resetToken = null) => new()
    {
        IsSuccess = true,
        ResetToken = resetToken
    };

    public static RequestPasswordResetResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}

/// <summary>
/// 重置密码命令
/// </summary>
public sealed record ResetPasswordCommand
{
    /// <summary>
    /// 重置令牌
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// 新密码
    /// </summary>
    public required string NewPassword { get; init; }
}

/// <summary>
/// 重置密码结果
/// </summary>
public sealed record ResetPasswordResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static ResetPasswordResult Success() => new()
    {
        IsSuccess = true
    };

    public static ResetPasswordResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
