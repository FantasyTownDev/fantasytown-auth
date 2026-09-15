using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil validate 端点
/// POST /api/yggdrasil/authserver/validate
/// </summary>
public static class ValidateEndpoint
{
    public static void MapValidate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/validate", async (HttpContext context) =>
        {
            if (!context.Request.ContentType?.Contains("application/json") == true)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.UnsupportedMediaType());
                return;
            }

            ValidateRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ValidateRequest>();
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

            var handler = context.RequestServices.GetRequiredService<ValidateHandler>();
            var result = await handler.HandleAsync(request.AccessToken, request.ClientToken);

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
