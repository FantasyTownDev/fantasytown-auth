using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil refresh 请求体
/// </summary>
public sealed record RefreshRequest
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("clientToken")]
    public string? ClientToken { get; init; }

    [JsonPropertyName("selectedProfile")]
    public ProfileInfo? SelectedProfile { get; init; }
}
