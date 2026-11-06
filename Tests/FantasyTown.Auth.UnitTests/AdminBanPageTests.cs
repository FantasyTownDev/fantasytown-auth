// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlayersBanModel = FantasyTown.Auth.Pages.Admin.Players.BanModel;
using UsersBanModel = FantasyTown.Auth.Pages.Admin.Users.BanModel;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// M3 回归：两个 Ban 页 Until 解析失败此前静默落 until=null → 管理员意图临时却执行
/// 永久封禁——须拒绝提交并 TempData 报错，不触达领域命令。
/// </summary>
public class AdminBanPageTests
{
    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private static void SetupHttpContext(PageModel page)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITempDataDictionaryFactory>(new StubTempDataFactory());
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        page.PageContext = new PageContext(new ActionContext(context, new RouteData(), new ActionDescriptor()));
    }

    [Fact]
    public async Task PlayersBan_MalformedUntil_RejectsWithoutPersisting()
    {
        using var db = CreateInMemoryDbContext();
        var owner = new User { Email = "mod@test.com", Password = "hashed", Permission = UserPermission.ServerModerator, RegisterAt = DateTime.UtcNow };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var player = new Player { Uid = owner.Uid, Name = "BannedOne", Uuid = "ABCDEF0123456789ABCDEF0123456789" };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var model = new PlayersBanModel(
            new BanPlayerHandler(db, new RecordingPlayerCacheInvalidation(), new RecordingTokenRevocation()),
            new InMemoryPermissionSnapshot());
        SetupHttpContext(model);
        model.Until = "garbage-timestamp";

        var result = await model.OnPostAsync(player.Pid, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Detail", redirect.PageName);
        Assert.Contains("截止时间格式无效", Assert.IsType<string>(model.TempData["Error"]));
        // 领域命令未触达：无封禁日志、玩家未置封禁
        Assert.Empty(await db.AuthLogs.ToListAsync());
        var reloaded = await db.Players.FirstAsync(p => p.Pid == player.Pid);
        Assert.False(reloaded.IsBanned);
    }

    [Fact]
    public async Task UsersBan_MalformedUntil_RejectsWithoutPersisting()
    {
        using var db = CreateInMemoryDbContext();
        db.Users.Add(new User { Email = "victim@test.com", Password = "hashed", Permission = UserPermission.NormalPlayer, RegisterAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var model = new UsersBanModel(
            new BanUserHandler(db, new InMemoryPermissionSnapshot(), new RecordingTokenRevocation()),
            new InMemoryPermissionSnapshot());
        SetupHttpContext(model);
        model.Until = "99-99-99";

        var result = await model.OnPostAsync(1001, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Detail", redirect.PageName);
        Assert.Contains("截止时间格式无效", Assert.IsType<string>(model.TempData["Error"]));
        var target = await db.Users.FirstAsync();
        Assert.False(target.IsBanned);
    }

    // ---------- TempData 桩 ----------

    private sealed class StubTempDataFactory : ITempDataDictionaryFactory
    {
        private readonly StubTempDataProvider _provider = new();
        public ITempDataDictionary GetTempData(HttpContext context) => new TempDataDictionary(context, _provider);
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
