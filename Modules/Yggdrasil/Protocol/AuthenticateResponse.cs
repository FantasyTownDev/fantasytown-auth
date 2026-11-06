// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil authenticate 成功响应
/// </summary>
public sealed record AuthenticateResponse
{
    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("clientToken")]
    public required string ClientToken { get; init; }

    [JsonPropertyName("availableProfiles")]
    public required IReadOnlyList<ProfileInfo> AvailableProfiles { get; init; }

    [JsonPropertyName("selectedProfile")]
    public ProfileInfo? SelectedProfile { get; init; }

    [JsonPropertyName("user")]
    public UserPayload? User { get; init; }
}

/// <summary>
/// 简要玩家信息（id + name）
/// </summary>
public sealed record ProfileInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// requestUser=true 时返回的 user 负载
/// </summary>
public sealed record UserPayload
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("properties")]
    public IReadOnlyList<UserProperty> Properties { get; init; } = Array.Empty<UserProperty>();
}

public sealed record UserProperty
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}
