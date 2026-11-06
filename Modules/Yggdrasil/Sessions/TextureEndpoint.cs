// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// 皮肤纹理服务端点
/// GET /textures/skins/{uuid}.png — 返回玩家皮肤 PNG 图片
/// 注意：Minecraft 客户端不跟随重定向——/textures/* 在 Program.cs 经 UseWhen
/// 豁免 HTTPS 重定向（T3-008），勿在本端点前放置会重定向的中间件
/// </summary>
public static class TextureEndpoint
{
    public static void MapTextures(this IEndpointRouteBuilder app)
    {
        app.MapGet("/textures/skins/{uuid}.png", async (HttpContext context) =>
        {
            var uuid = context.Request.RouteValues["uuid"]?.ToString();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TextureEndpoint");
            logger.LogDebug("[TEXTURE] Request: {Uuid} from {Ip}", uuid,
                context.RequestServices.GetRequiredService<IClientIp>().GetClientIp());

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
            logger.LogDebug("[TEXTURE] Serving {Uuid}, size={Size} bytes", normalizedUuid, skinBytes.Length);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = skinBytes.Length;
            context.Response.Headers.CacheControl = "public, max-age=3600";
            await context.Response.Body.WriteAsync(skinBytes);
        });
    }
}
