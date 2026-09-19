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
}
