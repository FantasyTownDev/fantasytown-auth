using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil hasJoined 端点
/// GET /api/yggdrasil/sessionserver/session/minecraft/hasJoined?username={name}&serverId={id}
/// </summary>
public static class HasJoinedEndpoint
{
    public static void MapHasJoined(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/yggdrasil/sessionserver/session/minecraft/hasJoined", async (HttpContext context) =>
        {
            // 读取查询参数
            var username = context.Request.Query["username"].FirstOrDefault();
            var serverId = context.Request.Query["serverId"].FirstOrDefault();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(serverId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // 调用处理器
            var handler = context.RequestServices.GetRequiredService<HasJoinedHandler>();
            var result = await handler.HandleAsync(username, serverId);

            if (result.IsValid && result.Profile != null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;

                // 设置 ETag（基于 lastModified）
                var lastModified = result.Profile.Properties
                    .Where(p => p.Name == "textures")
                    .Select(p =>
                    {
                        try
                        {
                            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p.Value));
                            using var doc = JsonDocument.Parse(payload);
                            return doc.RootElement.GetProperty("timestamp").GetInt64();
                        }
                        catch
                        {
                            return 0L;
                        }
                    })
                    .FirstOrDefault();

                if (lastModified > 0)
                {
                    context.Response.Headers.ETag = $"\"{lastModified}\"";
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
