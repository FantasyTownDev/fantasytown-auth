using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil refresh 端点
/// POST /api/yggdrasil/authserver/refresh
/// </summary>
public static class RefreshEndpoint
{
    public static void MapRefresh(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/refresh", async (HttpContext context) =>
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

            RefreshRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<RefreshRequest>(rawBody, options);
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

            var handler = context.RequestServices.GetRequiredService<RefreshHandler>();
            var result = await handler.HandleAsync(request.AccessToken, request.ClientToken, request.SelectedProfile?.Id);

            if (result.IsValid && result.Response != null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsJsonAsync(result.Response);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.InvalidToken());
            }
        });
    }
}
