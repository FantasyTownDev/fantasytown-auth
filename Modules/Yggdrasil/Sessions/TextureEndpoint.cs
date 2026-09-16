using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 皮肤纹理服务端点
/// GET /textures/skins/{uuid}.png — 返回玩家皮肤 PNG 图片
/// 
/// 测试用法：
/// 1. 在项目根目录创建 textures/skins/ 文件夹
/// 2. 将玩家皮肤 PNG 文件命名为 {uuid}.png（如 abc123def...png）
/// 3. 皮肤尺寸必须为 64x64 或 64x32（标准 Minecraft 皮肤）
/// </summary>
public static class TextureEndpoint
{
    public static void MapTextures(this IEndpointRouteBuilder app, string skinBaseUrl)
    {
        app.MapGet("/textures/skins/{uuid}.png", async (HttpContext context) =>
        {
            var uuid = context.Request.RouteValues["uuid"]?.ToString();
            if (string.IsNullOrEmpty(uuid))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // 安全校验：只允许十六进制字符和短横线
            if (!System.Text.RegularExpressions.Regex.IsMatch(uuid, @"^[0-9a-f\-]+$"))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // 从本地文件读取皮肤
            var skinPath = Path.Combine("textures", "skins", $"{uuid}.png");
            if (!File.Exists(skinPath))
            {
                // 返回默认皮肤（Steve）
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var skinBytes = await File.ReadAllBytesAsync(skinPath);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = skinBytes.Length;
            await context.Response.Body.WriteAsync(skinBytes);
        });
    }
}
