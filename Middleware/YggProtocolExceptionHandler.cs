// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FantasyTown.Auth.Middleware;

/// <summary>
/// Yggdrasil 协议路径异常守卫（M1）。
/// /api/yggdrasil/* 上任何未捕获异常在此转为 500 + 冻结 {error, errorMessage} JSON，
/// 而非冒泡到全局 UseExceptionHandler("/Error")（重执行返回 HTML/非 JSON 空响应，
/// 使协议错误格式在故障面失守）。必须注册在 UseExceptionHandler 之后（内层），
/// 否则异常已被全局处理器截获，本守卫收不到。
/// </summary>
public sealed class YggProtocolExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<YggProtocolExceptionHandler> _logger;

    public YggProtocolExceptionHandler(RequestDelegate next, ILogger<YggProtocolExceptionHandler> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/yggdrasil"))
        {
            await _next(context);
            return;
        }

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on Yggdrasil path {Path}", context.Request.Path);

            // 响应已开始（端点已写部分输出）→ 无法改写状态，只能重抛交给服务器中止连接
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(YggErrorResponse.InternalServerError());
        }
    }
}
