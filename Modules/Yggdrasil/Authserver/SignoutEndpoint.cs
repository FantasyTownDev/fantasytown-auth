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
/// Yggdrasil signout 端点
/// POST /api/yggdrasil/authserver/signout
/// 验密通过 → 204；失败 → 403
/// </summary>
public static class SignoutEndpoint
{
    public static void MapSignout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/signout", async (HttpContext context) =>
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

            SignoutRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<SignoutRequest>(rawBody, options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            // 字段级 null 守卫（对齐 join/authenticate：required 对显式 null 不拦截）
            if (request.Username == null || request.Password == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            var handler = context.RequestServices.GetRequiredService<SignoutHandler>();
            var result = await handler.HandleAsync(request.Username, request.Password);

            if (result.IsValid)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.InvalidCredentials());
            }
        });
    }
}

/// <summary>
/// signout 请求体
/// </summary>
public sealed record SignoutRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public required string Username { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("password")]
    public required string Password { get; init; }
}
