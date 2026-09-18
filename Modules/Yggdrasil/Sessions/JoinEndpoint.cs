using System.Text;
using System.Text.Json;
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
            var result = await handler.HandleAsync(request.AccessToken, request.SelectedProfile.Id, request.ServerId);

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
    [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("selectedProfile")]
    public required JoinProfile SelectedProfile { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("serverId")]
    public required string ServerId { get; init; }
}

/// <summary>
/// join 中的 selectedProfile
/// </summary>
public sealed record JoinProfile
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public required string Id { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public required string Name { get; init; }
}
