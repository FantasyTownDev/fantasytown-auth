namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 站点级配置选项，绑定自 appsettings.json / 环境变量。
/// </summary>
public sealed record SiteOptions
{
    /// <summary>
    /// 站点 URL（验证为 HTTPS）。用于邮件模板、重定向。
    /// </summary>
    public string SiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// 每 IP 每小时注册配额。软配额：预检 + 后递增，含并发窗口。
    /// </summary>
    public int RegsPerIp { get; init; } = 3;

    /// <summary>
    /// 玩家名验证规则。可选值："official", "cjk", "utf8", "custom"。
    /// </summary>
    public string PlayerNameRule { get; init; } = "cjk";

    /// <summary>
    /// 玩家名长度范围（min-max，如 "3-16"）。
    /// </summary>
    public string PlayerNameLength { get; init; } = "3-16";

    /// <summary>
    /// 游戏登录（Ygg authenticate）是否要求邮箱验证。
    /// true 时未验证用户被 403，使用统一错误消息。
    /// 默认 false（兼容原 Blessing Skin 行为）。
    /// </summary>
    public bool RequireVerification { get; init; } = false;

    /// <summary>
    /// 种子所有者邮箱，用于首次引导。
    /// 仅生效一次（首次 ServerOwner 提权）。
    /// 应来自容器 Secret，非源代码。空字符串 = 禁用种子。
    /// </summary>
    public string SeedOwnerEmail { get; init; } = string.Empty;
}
