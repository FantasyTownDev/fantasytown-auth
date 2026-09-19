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
