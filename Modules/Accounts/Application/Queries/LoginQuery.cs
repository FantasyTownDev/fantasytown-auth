// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.Modules.Accounts.Application.Queries;

/// <summary>
/// 登录查询
/// </summary>
public sealed record LoginQuery
{
    /// <summary>
    /// 用户名
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// 密码
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// 客户端 IP
    /// </summary>
    public required string ClientIp { get; init; }

    /// <summary>
    /// 验证码（渐进式，后续实现）
    /// </summary>
    public string? CaptchaToken { get; init; }
}

/// <summary>
/// 登录查询结果
/// </summary>
public sealed record LoginResult
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
    /// 用户 ID（成功时返回）
    /// </summary>
    public int? UserId { get; init; }

    /// <summary>
    /// 用户名（成功时返回）
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// 权限（成功时返回）
    /// </summary>
    public UserPermission? Permission { get; init; }

    /// <summary>
    /// 是否被封禁
    /// </summary>
    public bool IsBanned { get; init; }

    /// <summary>
    /// 封禁剩余时间（秒）
    /// </summary>
    public double? BanRemainingSeconds { get; init; }

    /// <summary>
    /// 需要验证码
    /// </summary>
    public bool RequiresCaptcha { get; init; }

    public static LoginResult Success(int userId, string username, UserPermission permission) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Username = username,
        Permission = permission
    };

    public static LoginResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };

    /// <summary>
    /// 登录被拒（含封禁/锁定统一话术场景）。
    /// message：验密通过后揭示的封禁文案（V5 §6.5/§4.4 评审修订），未传时用兜底句。
    /// </summary>
    public static LoginResult Banned(double remainingSeconds, string? message = null) => new()
    {
        IsSuccess = false,
        IsBanned = true,
        BanRemainingSeconds = remainingSeconds,
        ErrorMessage = message ?? "账户已被封禁"
    };

    public static LoginResult RequiresCaptchaLogin() => new()
    {
        IsSuccess = false,
        RequiresCaptcha = true,
        ErrorMessage = "需要验证码"
    };
}
