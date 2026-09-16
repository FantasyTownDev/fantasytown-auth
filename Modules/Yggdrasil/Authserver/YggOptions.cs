namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// Yggdrasil 配置选项，绑定自 appsettings.json
/// </summary>
public sealed record YggOptions
{
    /// <summary>
    /// 令牌验证窗口（validate 有效期内）。默认 3 天。
    /// </summary>
    public TimeSpan TokenExpire1 { get; init; } = TimeSpan.FromDays(3);

    /// <summary>
    /// 令牌刷新窗口（refresh 有效期内）+ 缓存 TTL。默认 7 天。
    /// 约束：TokenExpire1 <= TokenExpire2
    /// </summary>
    public TimeSpan TokenExpire2 { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 每 IP 每分钟认证请求数上限。默认 30。
    /// </summary>
    public int AuthRateLimitPerIp { get; init; } = 30;

    /// <summary>
    /// UUID 生成算法。"v3" = 确定性（基于用户名），"v4" = 随机。
    /// </summary>
    public string UuidAlgorithm { get; init; } = "v3";

    /// <summary>
    /// 皮肤纹理基础 URL。用于构建 textures SKIN url。
    /// 示例：http://localhost:5001
    /// </summary>
    public string SkinBaseUrl { get; init; } = "http://localhost:5001";

    /// <summary>
    /// 皮肤域名白名单。Minecraft 客户端校验纹理 URL 是否在此列表中。
    /// 示例：["192.168.5.230", "example.com"]
    /// </summary>
    public string[] SkinDomains { get; init; } = [];
}
