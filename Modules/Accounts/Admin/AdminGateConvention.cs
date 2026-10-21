using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// /Admin 文件夹一刀切装配（V5 §7.1 原则 5）：
/// 文件夹内每个页面自动附加 <see cref="ManagementGateFilter"/>，
/// 未声明 <see cref="ManagementGateAttribute"/> 的 POST 由过滤器 fail-closed 拒绝。
/// 与 AuthorizeFolder("moderator") 文件夹级策略兜底并列装配（实施指导 §2.3 第 4 节）。
/// </summary>
public sealed class AdminGateConvention : IPageApplicationModelConvention
{
    public void Apply(PageApplicationModel model)
    {
        // 路由模板形如 "Admin"（仪表盘）或 "Admin/Users/{uid}/Delete"（操作页）
        var template = model.RouteTemplate;
        if (string.Equals(template, "Admin", StringComparison.OrdinalIgnoreCase)
            || (template?.StartsWith("Admin/", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            model.Filters.Add(new TypeFilterAttribute<ManagementGateFilter>());
        }
    }
}
