using System.Net;
using System.Text.RegularExpressions;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 冻结项 4（V5 §7.1 管线顺序 / 防错 #12）HTTP 层钉死测试。
/// 用 WebApplicationFactory 启完整 app（独立库 fantasytown_http_test 由 app 自身迁移管线建表），经真实管线验证：
/// <list type="bullet">
/// <item>已认证 + 无 antiforgery token 的 POST → 400，先于闸门 403 与 handler 领域命令（400 是唯一可能来源：antiforgery）；</item>
/// <item>已认证 + 合法 token 但注定越权（自封禁）→ 才到达闸门得 403——400 严格先于 403；</item>
/// <item>匿名 POST → 中间件挑战 302 → /Account/Login（框架装配：UseAuthorization 中间件先于页面管线；同样不进入闸门/领域命令）。</item>
/// </list>
/// 这是单元级闸门测试无法覆盖的框架层顺序保证。
/// </summary>
[Collection("MariaDB")]
public class AntiforgeryPipelineOrderTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        // 完整 app 启动需 DB + Redis：用环境变量覆盖连接串（最高优先级），指向独立的 HTTP 测试库——
        // 共享的 fantasytown_integration_test 由 EnsureCreated 建表（无 _EFMigrationsHistory），
        // app 启动的 MigrateAsync 会撞"表已存在"；独立库让 app 自己的迁移管线从零建 schema。
        // Redis 沿用默认 localhost:6379（PERM 快照回源 / DataProtection 密钥环）。
        var baseConn = TestDbContextFactory.GetConnectionString();
        var httpConn = baseConn.Replace(
            "Database=fantasytown_integration_test", "Database=fantasytown_http_test");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", httpConn);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>
    /// 认证 cookie 为 Secure（__Host-ft_auth + CookieSecurePolicy.Always），只回传 https——
    /// TestServer 基址必须 https://localhost，否则登录 cookie 不被带回、请求恒为匿名。
    /// </summary>
    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        // 断言首响应状态码：任何 302 跳转跟随都会掩盖真实拒绝阶段
        AllowAutoRedirect = false,
    });

    private sealed record SeededUser(int Uid, string Email, string Password);

    private async Task<SeededUser> SeedUserAsync(UserPermission permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        const string password = "Antiforgery-Test-1!";
        var user = new User
        {
            Email = $"antiforgery-{Guid.NewGuid():N}@it.local",
            Password = passwordService.HashPassword(password),
            Permission = permission,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Verified = true,
            RegisterAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new SeededUser(user.Uid, user.Email, password);
    }

    private static async Task LoginAsync(HttpClient client, SeededUser user)
    {
        var get = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var token = ExtractAntiForgeryToken(await get.Content.ReadAsStringAsync());

        var post = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.Username"] = user.Email,
                ["Input.Password"] = user.Password,
                ["__RequestVerificationToken"] = token,
            }));

        // 登录成功 PRG：302 → /Index；Secure 认证 cookie 由 https 基址下的 CookieContainer 持有
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var tag = Regex.Match(html, "<input[^>]*__RequestVerificationToken[^>]*>");
        if (!tag.Success)
        {
            var forms = Regex.Matches(html, "<form[^>]*>");
            var formList = forms.Count == 0
                ? "页面无任何 <form> 元素"
                : string.Join(" ‖ ", forms.Select(f => f.Value));
            Assert.Fail($"页面表单未注入 __RequestVerificationToken（UI 无法提交，属 Step 4 缺陷）。{forms.Count} 个 form：{formList}");
        }
        var value = Regex.Match(tag.Value, "value=\"([^\"]+)\"");
        Assert.True(value.Success, "__RequestVerificationToken 缺少 value 属性");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    [Fact]
    public async Task PostAdminAction_AuthenticatedWithoutAntiforgeryToken_Is400BeforeGateAndHandler()
    {
        var client = CreateClient();
        var owner = await SeedUserAsync(UserPermission.ServerOwner);
        var target = await SeedUserAsync(UserPermission.NormalPlayer);
        await LoginAsync(client, owner);

        // 无令牌 POST 一个 owner→player 合法封禁（闸门本会放行、handler 会执行）：
        // 若 antiforgery 未先行，断言将得到 302（handler PRG）——400 是 antiforgery 阶段唯一来源
        var response = await client.PostAsync($"/admin/users/{target.Uid}/ban",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["reason"] = "pin" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // 深化断言：领域命令零副作用（handler 未执行）
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var reloaded = await db.Users.FindAsync(target.Uid);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsBanned);
    }

    [Fact]
    public async Task PostAdminAction_WithAntiforgeryToken_ReachesGateInstead_403OnSelfBan()
    {
        var client = CreateClient();
        var owner = await SeedUserAsync(UserPermission.ServerOwner);
        var target = await SeedUserAsync(UserPermission.NormalPlayer);
        await LoginAsync(client, owner);

        // owner 查看 target 详情：操作表单渲染 → 同时钉死 Step 4 UI 令牌注入（否则登录/提交全 400）
        var page = await client.GetAsync($"/admin/users/{target.Uid}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = ExtractAntiForgeryToken(await page.Content.ReadAsStringAsync());

        // 与上一测试同一闸门语义的 A/B：带合法 token 的自封禁（注定 NotSelf 拒）穿过 antiforgery
        // 后由闸门 403 拒绝——有 token 得 403、无 token 得 400 ⇒ antiforgery 严格先于闸门
        var withToken = await client.PostAsync($"/admin/users/{owner.Uid}/ban",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
            }));

        if (withToken.StatusCode != HttpStatusCode.Forbidden)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var ownerAfter = db.Users.Find(owner.Uid);
            Assert.Fail($"期望 403（闸门拒自封禁），实际 {(int)withToken.StatusCode}。" +
                $"Location={withToken.Headers.Location}，owner.IsBanned={ownerAfter?.IsBanned}" +
                "（302 指向 Detail ⇒ ManagementGateFilter 未挂载，handler 纵深防御 PRG）");
        }
    }

    [Fact]
    public async Task PostAdminAction_Anonymous_IsChallengedToLogin_302BeforePagePipeline()
    {
        var client = CreateClient();

        // 匿名 POST：UseAuthorization 中间件挑战（LoginPath=/Account/Login）先于页面管线的
        // antiforgery/闸门（框架装配决定，页面级阶段无法先于中间件）——请求止步 302，
        // 同样不进入闸门/领域命令；已认证会话的 400 先于 403 由上面两测试钉死
        var response = await client.PostAsync("/admin/users/1/ban",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }
}
