using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil validate 请求体
/// </summary>
public sealed record ValidateRequest
{
    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("clientToken")]
    public string? ClientToken { get; init; }
}
