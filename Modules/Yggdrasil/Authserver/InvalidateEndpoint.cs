using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil invalidate 端点
/// POST /api/yggdrasil/authserver/invalidate
/// 始终返回 204（不验证 clientToken）
/// </summary>
public static class InvalidateEndpoint
{
    public static void MapInvalidate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/invalidate", async (HttpContext context) =>
        {
            InvalidateRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<InvalidateRequest>();
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            if (request != null)
            {
                var handler = context.RequestServices.GetRequiredService<InvalidateHandler>();
                await handler.HandleAsync(request.AccessToken);
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
    }
}

/// <summary>
/// invalidate 请求体
/// </summary>
public sealed record InvalidateRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }
}
