// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil authenticate 请求体
/// </summary>
public sealed record AuthenticateRequest
{
    [JsonPropertyName("agent")]
    public AgentInfo? Agent { get; init; }

    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }

    [JsonPropertyName("clientToken")]
    public string? ClientToken { get; init; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("requestUser")]
    public bool RequestUser { get; init; }
}

/// <summary>
/// Agent 信息（Minecraft = name + version）
/// </summary>
public sealed record AgentInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Minecraft";

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;
}
