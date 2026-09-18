using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// authlib-injector 元数据端点
/// GET /api/yggdrasil
/// 返回服务器名称、版本、签名公钥、皮肤域名
/// </summary>
public static class MetadataEndpoint
{
    public static void MapMetadata(this IEndpointRouteBuilder app, string publicKeyBase64, YggOptions yggOptions)
    {
        app.MapGet("/api/yggdrasil", async (HttpContext context) =>
        {
            var skinDomains = new List<string>
            {
                "textures.minecraft.net",
                "http://textures.minecraft.net",
                "https://textures.minecraft.net"
            };
            // 添加配置的域名 + 带协议的变体（authlib-injector 兼容）
            foreach (var domain in yggOptions.SkinDomains)
            {
                skinDomains.Add(domain);
                skinDomains.Add($"http://{domain}");
                skinDomains.Add($"https://{domain}");
            }

            var metadata = new
            {
                meta = new
                {
                    serverName = "FantasyTown",
                    serverId = 1,
                    implementationName = "FantasyTown.Auth",
                    implementationVersion = "1.0.0"
                },
                skinDomains = skinDomains.Distinct().ToArray(),
                signaturePublicKeys = new[]
                {
                    publicKeyBase64
                }
            };

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(metadata);
        });
    }
}
