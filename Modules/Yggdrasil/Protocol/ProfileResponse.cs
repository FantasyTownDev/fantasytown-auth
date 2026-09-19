using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil profile/{uuid} 响应体（含纹理签名）
/// </summary>
public sealed record ProfileResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("properties")]
    public required IReadOnlyList<ProfileProperty> Properties { get; init; }
}

/// <summary>
/// Profile 属性（name + value + 可选 signature）
/// </summary>
public sealed record ProfileProperty
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }
}
