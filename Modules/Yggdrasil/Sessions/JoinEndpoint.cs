using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil join 端点
/// POST /api/yggdrasil/sessionserver/session/minecraft/join
/// </summary>
public static class JoinEndpoint
{
    public static void MapJoin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/sessionserver/session/minecraft/join", async (HttpContext context) =>
        {
            context.Request.EnableBuffering();

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync();

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JoinEndpoint");
            logger.LogWarning("[JOIN] Raw body: {Body}", rawBody);

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            JoinRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<JoinRequest>(rawBody, options);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[JOIN] JSON parse failed");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            if (request == null)
            {
                logger.LogWarning("[JOIN] Deserialized to null");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            logger.LogWarning("[JOIN] AccessToken={Token}, ServerId={ServerId}", request.AccessToken, request.ServerId);

            // 调用处理器
            var handler = context.RequestServices.GetRequiredService<JoinHandler>();
            var result = await handler.HandleAsync(request.AccessToken, request.GetProfileId(), request.ServerId);

            if (result.IsValid)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.InvalidToken());
            }
        });
    }
}

/// <summary>
/// join 请求体
/// </summary>
public sealed record JoinRequest
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("selectedProfile")]
    public JsonElement SelectedProfileElement { get; init; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; init; }

    /// <summary>
    /// selectedProfile 可能是字符串（authlib-injector）或对象（标准 Yggdrasil）
    /// </summary>
    public string GetProfileId()
    {
        if (SelectedProfileElement.ValueKind == JsonValueKind.String)
            return SelectedProfileElement.GetString()!;
        
        if (SelectedProfileElement.ValueKind == JsonValueKind.Object &&
            SelectedProfileElement.TryGetProperty("id", out var id))
            return id.GetString()!;

        return string.Empty;
    }
}

/// <summary>
/// join 中的 selectedProfile（兼容标准格式）
/// </summary>
public sealed record JoinProfile
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
