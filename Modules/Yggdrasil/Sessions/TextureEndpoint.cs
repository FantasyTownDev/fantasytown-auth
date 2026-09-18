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
            if (string.IsNullOrEmpty(uuid))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // 安全校验：只允许十六进制字符和短横线（大小写无关）
            if (!System.Text.RegularExpressions.Regex.IsMatch(uuid, @"^[0-9a-fA-F\-]+$"))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // 归一化为小写（文件存储为小写）
            var normalizedUuid = uuid.ToLowerInvariant();

            var skinPath = Path.Combine("textures", "skins", $"{normalizedUuid}.png");
            if (!File.Exists(skinPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var skinBytes = await File.ReadAllBytesAsync(skinPath);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = skinBytes.Length;
            context.Response.Headers.CacheControl = "public, max-age=3600";
            await context.Response.Body.WriteAsync(skinBytes);
        });
    }
}
