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
/// Yggdrasil profile/{uuid} 端点
/// GET /api/yggdrasil/sessionserver/session/minecraft/profile/{uuid}
/// </summary>
public static class ProfileEndpoint
{
    public static void MapProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/yggdrasil/sessionserver/session/minecraft/profile/{uuid}", async (HttpContext context) =>
        {
            var uuid = context.Request.RouteValues["uuid"]?.ToString();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProfileEndpoint");
            logger.LogDebug("[PROFILE] Request: uuid={Uuid} from {Ip}", uuid,
                context.RequestServices.GetRequiredService<IClientIp>().GetClientIp());

            if (string.IsNullOrEmpty(uuid))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            uuid = uuid.ToUpperInvariant();

            var handler = context.RequestServices.GetRequiredService<ProfileHandler>();
            var result = await handler.HandleAsync(uuid);

            if (result.IsValid && result.Profile != null)
            {
                logger.LogDebug("[PROFILE] Found: uuid={Uuid}, name={Name}, props={Count}",
                    uuid, result.Profile.Name, result.Profile.Properties.Count);
                context.Response.StatusCode = StatusCodes.Status200OK;

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
                logger.LogWarning("[PROFILE] NOT found: uuid={Uuid}", uuid);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        });
    }
}
