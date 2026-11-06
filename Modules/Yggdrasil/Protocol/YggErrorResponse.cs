// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil 协议统一错误响应。
/// 冻结字段：error (错误类型) + errorMessage (人类可读消息)。
/// </summary>
public sealed record YggErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("errorMessage")]
    public required string ErrorMessage { get; init; }

    public static YggErrorResponse InvalidCredentials() => new()
    {
        Error = "ForbiddenOperationException",
        ErrorMessage = "Invalid credentials. Invalid username or password."
    };

    public static YggErrorResponse InvalidToken() => new()
    {
        Error = "ForbiddenOperationException",
        ErrorMessage = "Invalid token."
    };

    public static YggErrorResponse Forbidden(string message = "Forbidden.") => new()
    {
        Error = "ForbiddenOperationException",
        ErrorMessage = message
    };

    public static YggErrorResponse UnsupportedMediaType() => new()
    {
        Error = "Unsupported Media Type",
        ErrorMessage = "Invalid request body. Content-Type must be application/json."
    };

    /// <summary>
    /// 请求体缺失/JSON 解析失败（T3-015：统一走冻结错误格式，
    /// error 用 Yggdrasil 官方 IllegalArgumentException 类型，不再内联匿名对象）。
    /// </summary>
    public static YggErrorResponse MalformedRequest() => new()
    {
        Error = "IllegalArgumentException",
        ErrorMessage = "Invalid request body."
    };

    /// <summary>
    /// 服务端未预期异常（M1：/api/yggdrasil 路径异常兜底——客户端仍得冻结
    /// {error, errorMessage} 结构而非非 JSON 空响应；authlib 官方 error 类型）。
    /// </summary>
    public static YggErrorResponse InternalServerError() => new()
    {
        Error = "InternalServerErrorException",
        ErrorMessage = "Internal server error."
    };
}
