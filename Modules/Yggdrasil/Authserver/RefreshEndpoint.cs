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
            if (!context.Request.ContentType?.Contains("application/json") == true)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

            RefreshRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<RefreshRequest>();
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
