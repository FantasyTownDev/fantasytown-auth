using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil profile/{uuid} 端点
/// GET /sessionserver/session/minecraft/profile/{uuid}
/// 支持 CacheOutput + ETag=lastModified
/// </summary>
public static class ProfileEndpoint
{
    public static void MapProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sessionserver/session/minecraft/profile/{uuid}", async (HttpContext context) =>
        {
            var uuid = context.Request.RouteValues["uuid"]?.ToString();
            if (string.IsNullOrEmpty(uuid))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // 调用处理器
            var handler = context.RequestServices.GetRequiredService<ProfileHandler>();
            var result = await handler.HandleAsync(uuid);

            if (result.IsValid && result.Profile != null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;

                // 设置 ETag（基于 lastModified）
                if (result.LastModified > 0)
                {
                    context.Response.Headers.ETag = $"\"{result.LastModified}\"";
                }

                await context.Response.WriteAsJsonAsync(new
                {
                    id = result.Profile.Uuid,
                    name = result.Profile.Name,
                    properties = result.Profile.Properties.Select(p => new
                    {
                        name = p.Name,
                        value = p.Value,
                        signature = p.Signature
                    })
                });
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        });
    }
}
