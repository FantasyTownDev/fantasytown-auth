// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json;
using FantasyTown.Auth.Modules.Shared;
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
            var username = context.Request.Query["username"].FirstOrDefault();
            var serverId = context.Request.Query["serverId"].FirstOrDefault();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HasJoinedEndpoint");
            logger.LogDebug("[HASJOINED] Request: username={Username}, serverId={ServerId}, ip={Ip}",
                username, serverId, context.RequestServices.GetRequiredService<IClientIp>().GetClientIp());

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(serverId))
            {
                logger.LogWarning("[HASJOINED] Missing params: username={Username}, serverId={ServerId}", username, serverId);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var handler = context.RequestServices.GetRequiredService<HasJoinedHandler>();
            var result = await handler.HandleAsync(username, serverId);

            if (result.IsValid && result.Profile != null)
            {
                logger.LogDebug("[HASJOINED] Success: uuid={Uuid}, name={Name}, props={Count}",
                    result.Profile.Uuid, result.Profile.Name, result.Profile.Properties.Count);
                context.Response.StatusCode = StatusCodes.Status200OK;

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
                logger.LogWarning("[HASJOINED] NOT found: username={Username}, serverId={ServerId}", username, serverId);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        });
    }
}
