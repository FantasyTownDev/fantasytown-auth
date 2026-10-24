namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// /admin 写操作页的管线闸门声明（V5 §7.1/§7.3，标于 PageModel 类）：
/// 声明该页 POST 的越权矩阵档位、★敏感标记与审计操作标识。
/// 未声明闸门的 /admin POST 被 <see cref="ManagementGateFilter"/> fail-closed 拒绝——
/// "一切管理动作走统一管线"在结构上成立（隐藏按钮 ≠ 授权，判定永远在服务端）。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ManagementGateAttribute : Attribute, Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata
{
    /// <summary>越权矩阵档位</summary>
    public ManagementMatrix Matrix { get; }

    /// <summary>★敏感操作：表单须携带 actor 当前密码（V4 评审 R9）</summary>
    public bool Sensitive { get; set; }

    /// <summary>审计操作标识（如 "permission-change"；admin-denied 与确认失败审计的 context）</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>目标 UID 的路由键（§7.3 路由表：用户域为 "uid"）</summary>
    public string RouteKey { get; set; } = "uid";

    /// <summary>★表单中 actor 当前密码的字段名</summary>
    public string PasswordFormField { get; set; } = "currentPassword";

    public ManagementGateAttribute(ManagementMatrix matrix) => Matrix = matrix;
}
