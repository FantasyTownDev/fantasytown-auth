using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil join 端点
/// POST /sessionserver/session/minecraft/join
/// </summary>
public static class JoinEndpoint
{
    public static void MapJoin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sessionserver/session/minecraft/join", async (HttpContext context) =>
        {
            // Content-Type 检查
            if (!context.Request.ContentType?.Contains("application/json") == true)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

            // 读取请求体
            JoinRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<JoinRequest>();
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

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
