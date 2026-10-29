// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Security.Claims;
using FantasyTown.Auth.Modules.Accounts.Admin;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// 管理闸门过滤器测试：POST 无闸门声明 fail-closed、GET 透传、路由缺失 404、
/// 矩阵拒绝 403、放行时执行 handler（next）、★确认失败 403。
/// </summary>
public class ManagementGateFilterTests
{
    private const string OwnerPasswordHash = "hashed_Secret1!";

    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    private static void SeedUser(AuthDbContext db, int uid, string email, UserPermission permission, string passwordHash = "hashed_x")
    {
        db.Users.Add(new User
        {
            Uid = uid,
            Email = email,
            Password = passwordHash,
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        db.SaveChanges();
    }

    private static ClaimsPrincipal Principal(int uid)
    {
        var identity = new ClaimsIdentity(new[] { new Claim("uid", uid.ToString()) }, "test");
        return new ClaimsPrincipal(identity);
    }

    private static HttpContext CreateHttpContext(string method, ClaimsPrincipal? user = null, IFormCollection? form = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = "/admin/users/2/delete";
        if (user != null)
            http.User = user;
        if (form != null)
            http.Request.Form = form;
        return http;
    }

    private static (PageHandlerExecutingContext Context, NextSpy Spy) CreatePageContext(
        HttpContext http, RouteValueDictionary routeValues, params IFilterMetadata[] filters)
    {
        var descriptor = new ActionDescriptor();
        var actionContext = new ActionContext(http, new RouteData(routeValues), descriptor);
        var pageContext = new PageContext(actionContext);
        var context = new PageHandlerExecutingContext(
            pageContext,
            filters,
            null,
            new Dictionary<string, object?>(),
            new TestPageModel());
        return (context, new NextSpy(pageContext));
    }

    private sealed class TestPageModel : PageModel
    {
    }

    /// <summary>next 委托哨兵：记录 handler 是否被执行（阶段五）</summary>
    private sealed class NextSpy
    {
        private readonly PageContext _pageContext;
        public bool Executed { get; private set; }

        public NextSpy(PageContext pageContext) => _pageContext = pageContext;

        public Task<PageHandlerExecutedContext> Invoke()
        {
            Executed = true;
            return Task.FromResult(new PageHandlerExecutedContext(
                _pageContext, Array.Empty<IFilterMetadata>(), null, new TestPageModel()));
        }
    }

    private static ManagementPipeline CreatePipeline(AuthDbContext db) =>
        new(db, new InMemoryPasswordService(), new InMemoryLockoutService(), new StubClientIp());

    private sealed class StubClientIp : IClientIp
    {
        public string? GetClientIp() => "203.0.113.9";
    }

    [Fact]
    public async Task GetRequest_ExecutesHandler()
    {
        using var db = CreateInMemoryDbContext();
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var (context, spy) = CreatePageContext(
            CreateHttpContext("GET", Principal(1)),
            new RouteValueDictionary { ["uid"] = "2" });

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.True(spy.Executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task PostWithoutGateAttribute_DeniesFailClosed()
    {
        using var db = CreateInMemoryDbContext();
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1)),
            new RouteValueDictionary { ["uid"] = "2" });

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ManagementPipeline.DenialMessage, result.Value);
    }

    [Fact]
    public async Task PostWithIncompleteGateAttribute_DeniesFailClosed()
    {
        using var db = CreateInMemoryDbContext();
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser);   // Operation 未声明
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1)),
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task PostWithoutUidClaim_DeniesFailClosed()
    {
        using var db = CreateInMemoryDbContext();
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser) { Operation = "delete" };
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST"),   // 无 uid claim
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task PostWithoutRouteUid_Returns404()
    {
        using var db = CreateInMemoryDbContext();
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser) { Operation = "delete" };
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1)),
            new RouteValueDictionary(),   // 路由缺 uid
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task PostMatrixDenied_Returns403WithAudit()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "player@example.com", UserPermission.NormalPlayer);
        SeedUser(db, 2, "owner@example.com", UserPermission.ServerOwner);
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser) { Operation = "delete" };
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1)),
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ManagementPipeline.DenialMessage, result.Value);
        Assert.Contains(db.AuthLogs.Local, l => l.Action == "admin-denied" && l.ActorId == 1 && l.SubjectId == 2);
    }

    [Fact]
    public async Task PostAuthorized_ExecutesHandler()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "owner@example.com", UserPermission.ServerOwner);
        SeedUser(db, 2, "player@example.com", UserPermission.NormalPlayer);
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser) { Operation = "delete" };
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1)),
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.True(spy.Executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task PostSensitiveGate_WrongFormPassword_Returns403AndAudits()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "owner@example.com", UserPermission.ServerOwner, OwnerPasswordHash);
        SeedUser(db, 2, "player@example.com", UserPermission.NormalPlayer);
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser)
        {
            Operation = "delete",
            Sensitive = true
        };
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["currentPassword"] = "wrong-password"
        });
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1), form),
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.False(spy.Executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains(db.AuthLogs.Local, l =>
            l.Action == "admin-denied" && l.Context == "delete:confirmation");
    }

    [Fact]
    public async Task PostSensitiveGate_CorrectFormPassword_ExecutesHandler()
    {
        using var db = CreateInMemoryDbContext();
        SeedUser(db, 1, "owner@example.com", UserPermission.ServerOwner, OwnerPasswordHash);
        SeedUser(db, 2, "player@example.com", UserPermission.NormalPlayer);
        var filter = new ManagementGateFilter(CreatePipeline(db));
        var gate = new ManagementGateAttribute(ManagementMatrix.OwnerUser)
        {
            Operation = "delete",
            Sensitive = true
        };
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["currentPassword"] = "Secret1!"
        });
        var (context, spy) = CreatePageContext(
            CreateHttpContext("POST", Principal(1), form),
            new RouteValueDictionary { ["uid"] = "2" },
            gate);

        await filter.OnPageHandlerExecutionAsync(context, spy.Invoke);

        Assert.True(spy.Executed);
        Assert.Null(context.Result);
    }
}
