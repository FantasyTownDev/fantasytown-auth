using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 客户端 IP 提取抽象，支持受信任代理头解析。
/// 实现读取 HttpContext.Connection.RemoteIpAddress，
/// 在配置了 KnownProxies 的反向代理后获取真实客户端 IP。
/// </summary>
public interface IClientIp
{
    /// <summary>
    /// 获取当前 HTTP 上下文中的可信客户端 IP 地址。
    /// </summary>
    string? GetClientIp();
}

/// <summary>
/// 从 HttpContext 提取客户端 IP 的默认实现。
/// </summary>
public sealed class HttpContextClientIp : IClientIp
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextClientIp(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? GetClientIp()
    {
        return _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }
}
