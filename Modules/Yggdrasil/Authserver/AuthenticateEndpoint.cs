using System.Text.Json;
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
            // 1. 读取请求体
            AuthenticateRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<AuthenticateRequest>();
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "json", errorMessage = "Invalid request body." });
                return;
            }

            // 3. 获取客户端 IP
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // 4. IP 限流检查
            var ipLimiter = context.RequestServices.GetRequiredService<IRateLimiter>();
            if (!await ipLimiter.IsAllowedAsync(clientIp))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(YggErrorResponse.Forbidden("Rate limit exceeded."));
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
