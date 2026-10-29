// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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
            // 读取请求体
            using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            ValidateRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<ValidateRequest>(rawBody, options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            if (request == null || request.AccessToken == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
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
