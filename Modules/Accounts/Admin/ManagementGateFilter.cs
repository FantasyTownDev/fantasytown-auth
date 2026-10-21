using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// 管理管线 HTTP 层装配（实施指导 §3.4-2：filter 组合而非散落 handler）。
/// 阶段一（Antiforgery，Razor Pages 内置）与阶段二（§5.2 快照策略，AuthorizeFolder/[Authorize]）
/// 由框架先行；本过滤器实现阶段三/四（越权矩阵 → ★二次确认），放行后 handler 执行阶段五（领域命令，
/// 内部闸门为纵深防御，§7.1 原则 3）。
/// </summary>
public sealed class ManagementGateFilter : IAsyncPageFilter, IOrderedFilter
{
    /// <summary>
    /// 阶段顺序保障（冻结项 4）：置于默认优先级（0）之后，确保框架 Antiforgery
    /// 先于矩阵/确认阶段执行——注定 400 的伪造请求不会进入闸门；
    /// Step 4 以集成测试钉死（无令牌 POST → 400 先于 403 闸门）。
    /// </summary>
    public int Order => 100;

    private readonly ManagementPipeline _pipeline;

    public ManagementGateFilter(ManagementPipeline pipeline) => _pipeline = pipeline;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        // 阶段闸门只约束写操作：读页面仅受策略约束（V5 §7.2 只读行无目标约束）
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        var gate = context.Filters.OfType<ManagementGateAttribute>().FirstOrDefault();
        if (gate == null || string.IsNullOrWhiteSpace(gate.Operation))
        {
            // 结构性兜底：/admin 下未声明闸门（或声明不完整）的 POST 一律拒绝
            context.Result = Deny(ManagementPipeline.DenialMessage);
            return;
        }

        if (context.HttpContext.User.GetUid() is not { } actorUid)
        {
            context.Result = Deny(ManagementPipeline.DenialMessage);
            return;
        }

        if (context.RouteData.Values[gate.RouteKey] is not string targetValue
            || !int.TryParse(targetValue, out var targetUid))
        {
            context.Result = new ObjectResult("资源不存在") { StatusCode = 404 };
            return;
        }

        string? actorCurrentPassword = null;
        if (gate.Sensitive)
        {
            // ★二次确认输入取自表单（Antiforgery 已先行校验）
            var form = await context.HttpContext.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            actorCurrentPassword = form[gate.PasswordFormField].ToString();
        }

        var result = await _pipeline.EvaluateAsync(new ManagementGateRequest
        {
            ActorUid = actorUid,
            TargetUid = targetUid,
            Operation = gate.Operation,
            Matrix = gate.Matrix,
            Sensitive = gate.Sensitive,
            ActorCurrentPassword = actorCurrentPassword
        }, context.HttpContext.RequestAborted);

        if (result.IsAuthorized)
        {
            // 阶段五：放行后 handler 执行领域命令（handler 内部闸门为纵深防御，§7.1 原则 3）
            await next();
            return;
        }

        // 两失败分支均 403 同文案（§7.1 原则 2：无信息泄露）；目标不存在 404
        context.Result = result.StatusCode == 404
            ? new ObjectResult(result.DenialReason) { StatusCode = 404 }
            : Deny(result.DenialReason ?? ManagementPipeline.DenialMessage);
    }

    private static ObjectResult Deny(string message) => new(message) { StatusCode = 403 };
}
