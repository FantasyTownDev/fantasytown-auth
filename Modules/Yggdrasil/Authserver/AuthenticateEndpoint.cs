// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text;
using System.Text.Json;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil authenticate 端点
/// POST /api/yggdrasil/authserver/authenticate
/// </summary>
public static class AuthenticateEndpoint
{
    public static void MapAuthenticate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/yggdrasil/authserver/authenticate", async (HttpContext context) =>
        {
            // 1. 读取原始请求体
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            // 2. 反序列化
            AuthenticateRequest? request;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                request = JsonSerializer.Deserialize<AuthenticateRequest>(rawBody, options);
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

            // 2.5 字段级 null 守卫（对齐 join：required 对显式 null 不拦截，
            //     穿透到 Argon2.Verify 会抛 ArgumentNullException → 非协议 500；
            //     且 username:null 会先写 FAIL_ACCT: 空后缀键污染键空间）
            if (request.Username == null || request.Password == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.MalformedRequest());
                return;
            }

            // 3. 获取客户端 IP（受信代理解析，见 V5 §8）
            var clientIp = context.RequestServices.GetRequiredService<IClientIp>().GetClientIp() ?? "unknown";

            // 4. IP 限流检查
            var ipLimiter = context.RequestServices.GetRequiredService<IRateLimiter>();
            if (!await ipLimiter.IsAllowedAsync(clientIp))
            {
                // T3-014：不向客户端泄露内部限流机制，统一 ForbiddenOperationException 默认文案
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.Forbidden());
                return;
            }

            // 5. 调用处理器
            var handler = context.RequestServices.GetRequiredService<AuthenticateHandler>();
            var result = await handler.HandleAsync(request, clientIp);

            // 5. 返回结果
            context.Response.StatusCode = result.StatusCode;
            if (result.IsSuccess && result.Response != null)
            {
                await context.Response.WriteAsJsonAsync(result.Response);
            }
            else if (result.Error != null)
            {
                await context.Response.WriteAsJsonAsync(result.Error);
            }
        });
    }
}
