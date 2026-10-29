// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil invalidate 端点
/// POST /api/yggdrasil/authserver/invalidate
/// 始终返回 204（不验证 clientToken）
/// </summary>
public static class InvalidateEndpoint
{
    public static void MapInvalidate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/invalidate", async (HttpContext context) =>
        {
            // 读取请求体
            using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            InvalidateRequest? request = null;
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    request = JsonSerializer.Deserialize<InvalidateRequest>(rawBody, options);
                }
                catch (JsonException)
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
            }

            if (request != null)
            {
                var handler = context.RequestServices.GetRequiredService<InvalidateHandler>();
                await handler.HandleAsync(request.AccessToken);
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
    }
}

/// <summary>
/// invalidate 请求体
/// </summary>
public sealed record InvalidateRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }
}
