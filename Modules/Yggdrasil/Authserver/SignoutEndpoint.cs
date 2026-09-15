using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil signout 端点
/// POST /api/yggdrasil/authserver/signout
/// 验密通过 → 204；失败 → 403
/// </summary>
public static class SignoutEndpoint
{
    public static void MapSignout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/signout", async (HttpContext context) =>
        {
            if (!context.Request.ContentType?.Contains("application/json") == true)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

            SignoutRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SignoutRequest>();
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

            var handler = context.RequestServices.GetRequiredService<SignoutHandler>();
            var result = await handler.HandleAsync(request.Username, request.Password);

            if (result.IsValid)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.InvalidCredentials());
            }
        });
    }
}

/// <summary>
/// signout 请求体
/// </summary>
public sealed record SignoutRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public required string Username { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("password")]
    public required string Password { get; init; }
}
