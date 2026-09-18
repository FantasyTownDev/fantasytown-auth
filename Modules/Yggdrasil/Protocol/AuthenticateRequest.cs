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
