// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text;
using System.Text.Json;
using FantasyTown.Auth.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// M1 回归：/api/yggdrasil 路径未捕获异常必须转 500 协议 JSON（冻结 {error, errorMessage}），
/// 不冒泡到全局 UseExceptionHandler("/Error")；非协议路径不受影响。
/// </summary>
public class YggProtocolExceptionHandlerTests
{
    private static DefaultHttpContext CreateContext(string path, MemoryStream? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Post;
        context.Response.Body = body ?? new MemoryStream();
        return context;
    }

    private static YggProtocolExceptionHandler CreateMiddleware(RequestDelegate next)
        => new(next, NullLogger<YggProtocolExceptionHandler>.Instance);

    [Fact]
    public async Task InvokeAsync_NonYggPath_PropagatesException()
    {
        // 非协议路径不拦截——异常照常冒泡给全局异常处理器
        var context = CreateContext("/Account/Login");
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_YggPath_NoException_PassesThrough()
    {
        var context = CreateContext("/api/yggdrasil/authserver/authenticate");
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode); // 默认未改写
    }

    [Fact]
    public async Task InvokeAsync_YggPath_Exception_ReturnsProtocolJson500()
    {
        var body = new MemoryStream();
        var context = CreateContext("/api/yggdrasil/sessionserver/join", body);
        var middleware = CreateMiddleware(_ => throw new TimeoutException("redis down"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);

        body.Position = 0;
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal("InternalServerErrorException", doc.RootElement.GetProperty("error").GetString());
        Assert.True(doc.RootElement.TryGetProperty("errorMessage", out _));
    }

    [Fact]
    public async Task InvokeAsync_YggPath_ResponseStarted_Rethrows()
    {
        var context = CreateContext("/api/yggdrasil/authserver/authenticate");
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedFeature());
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    /// <summary>HasStarted 只读——替换 feature 模拟响应已开始</summary>
    private sealed class StartedFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
