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
        // 全部 admin 页为纯 @page（§7.3 路由由 Program.cs AddPageRoute 挂在 PageRouteModel 上），
        // PageApplicationModel.RouteTemplate 恒为 null——改按 "page" route value
        // （页面应用内路径，形如 "/Admin/Users/Ban"）判定管理域归属。
        model.ActionDescriptor.RouteValues.TryGetValue("page", out var rawPage);
        var pagePath = (rawPage as string)?.TrimStart('/');
        if (string.Equals(pagePath, "Admin", StringComparison.OrdinalIgnoreCase)
            || (pagePath?.StartsWith("Admin/", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            model.Filters.Add(new TypeFilterAttribute<ManagementGateFilter>());
        }
    }
}
