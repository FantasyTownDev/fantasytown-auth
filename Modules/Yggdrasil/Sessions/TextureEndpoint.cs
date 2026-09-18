using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 皮肤纹理服务端点
/// GET /textures/skins/{uuid}.png — 返回玩家皮肤 PNG 图片
/// </summary>
public static class TextureEndpoint
{
    public static void MapTextures(this IEndpointRouteBuilder app)
    {
        app.MapGet("/textures/skins/{uuid}.png", async (HttpContext context) =>
        {
            var uuid = context.Request.RouteValues["uuid"]?.ToString();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TextureEndpoint");
            logger.LogInformation("[TEXTURE] Request: {Uuid} from {Ip}", uuid, context.Connection.RemoteIpAddress);

            if (string.IsNullOrEmpty(uuid))
            {
                logger.LogWarning("[TEXTURE] Empty UUID");
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(uuid, @"^[0-9a-fA-F\-]+$"))
            {
                logger.LogWarning("[TEXTURE] Invalid UUID format: {Uuid}", uuid);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var normalizedUuid = uuid.ToLowerInvariant();
            var skinPath = Path.Combine("textures", "skins", $"{normalizedUuid}.png");
            if (!File.Exists(skinPath))
            {
                logger.LogWarning("[TEXTURE] File NOT found: {Path}", skinPath);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var skinBytes = await File.ReadAllBytesAsync(skinPath);
            logger.LogInformation("[TEXTURE] Serving {Uuid}, size={Size} bytes", normalizedUuid, skinBytes.Length);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = skinBytes.Length;
            context.Response.Headers.CacheControl = "public, max-age=3600";
            await context.Response.Body.WriteAsync(skinBytes);
        });
    }
}
