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
            // 读取请求体
            using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            SignoutRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<SignoutRequest>(rawBody, options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
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
