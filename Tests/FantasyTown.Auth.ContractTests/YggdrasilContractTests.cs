using System.Net;
using System.Text;
using System.Text.Json;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyTown.Auth.ContractTests;

/// <summary>
/// Yggdrasil 协议契约测试（T3-010 重建）。
/// 经 WebApplicationFactory 打真实端点（此前仅内存序列化 DTO 与 fixture 比对、204 断言恒真，形同虚设）：
/// <list type="bullet">
/// <item>静态确定响应（错误 JSON）→ 原始字节逐字节比对 golden fixture；</item>
/// <item>204 类 → fixture 声明期望状态码 + 断言空响应体；</item>
/// <item>动态成功响应（token/签名/base64 等）→ 与骨架 fixture 做键级结构比对
/// （"&lt;dynamic&gt;" 占位动态值；键名、顺序、null 与字面值精确即协议契约）。</item>
/// </list>
/// 用独立库 fantasytown_contract_test（连接串由 IntegrationTests__ConnectionString 派生、经
/// ConnectionStrings__DefaultConnection 环境变量覆盖，app 自身迁移管线建表）；Redis 沿用默认 localhost:6379。
/// </summary>
public class YggdrasilContractTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        var baseConn = Environment.GetEnvironmentVariable("IntegrationTests__ConnectionString")
            ?? "Server=localhost;Port=3307;Database=fantasytown_integration_test;User=fantasytown;Password=ft_local_dev_2026;";
        var contractConn = baseConn.Replace(
            "Database=fantasytown_integration_test", "Database=fantasytown_contract_test");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", contractConn);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ---------- 种子 ----------

    private sealed record SeededAccount(string Email, string Password, string PlayerName, string PlayerUuid);

    private async Task<SeededAccount> SeedAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        const string password = "Contract-Test-1!";
        var email = $"contract-{Guid.NewGuid():N}@it.local";
        var name = $"Ct{Guid.NewGuid():N}"[..16]; // Minecraft 名约束：3-16 字母数字下划线
        var uuid = Guid.NewGuid().ToString("N").ToUpperInvariant(); // DB 约定：UUID 存大写（join 端点归一化）

        var user = new User
        {
            Email = email,
            Password = passwordService.HashPassword(password),
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Verified = true,
            RegisterAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.Players.Add(new Player { Uid = user.Uid, Name = name, Uuid = uuid });
        await db.SaveChangesAsync();

        return new SeededAccount(email, password, name, uuid);
    }

    // ---------- 请求辅助 ----------

    private HttpClient CreateClient() => _factory.CreateClient();

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<JsonDocument> AuthenticateAsync(HttpClient client, SeededAccount account)
    {
        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = account.Email,
            password = account.Password,
            agent = new { name = "Minecraft", version = 1 },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // ---------- 断言辅助 ----------

    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "fixtures");

    private static string LoadFixture(string fileName)
    {
        var path = Path.Combine(FixturesPath, fileName);
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return File.ReadAllText(path).Trim();
    }

    /// <summary>静态响应：原始字节逐字节比对 golden fixture</summary>
    private static async Task AssertMatchesGoldenAsync(HttpResponseMessage response, string fixtureName)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(LoadFixture(fixtureName), body.Trim());
    }

    /// <summary>204 类：fixture 声明期望状态码 + 断言空响应体</summary>
    private static async Task AssertEmptyWithFixtureStatusAsync(HttpResponseMessage response, string fixtureName)
    {
        Assert.Equal(int.Parse(LoadFixture(fixtureName)), (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(body), $"期望空响应体，实际: {body}");
    }

    /// <summary>
    /// 动态响应：与骨架 fixture 做键级结构比对——键名与顺序、null、字面值精确一致；
    /// 值为 "&lt;dynamic&gt;" 的位置仅断言标量类型（动态值：token/签名/base64 等）。
    /// </summary>
    private static async Task AssertMatchesShapeAsync(HttpResponseMessage response, string fixtureName)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var actualDoc = JsonDocument.Parse(body);
        using var expectedDoc = JsonDocument.Parse(LoadFixture(fixtureName));
        AssertShape(expectedDoc.RootElement, actualDoc.RootElement, "$");
    }

    private static void AssertShape(JsonElement expected, JsonElement actual, string path)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.True(actual.ValueKind == JsonValueKind.Object,
                    $"{path}: 期望对象，实际 {actual.ValueKind}");
                var expectedKeys = expected.EnumerateObject().Select(p => p.Name).ToList();
                var actualKeys = actual.EnumerateObject().Select(p => p.Name).ToList();
                Assert.Equal(expectedKeys, actualKeys);
                foreach (var prop in expected.EnumerateObject())
                {
                    AssertShape(prop.Value, actual.GetProperty(prop.Name), $"{path}.{prop.Name}");
                }
                break;

            case JsonValueKind.Array:
                Assert.True(actual.ValueKind == JsonValueKind.Array,
                    $"{path}: 期望数组，实际 {actual.ValueKind}");
                Assert.True(actual.GetArrayLength() >= expected.GetArrayLength(),
                    $"{path}: 数组长度 {actual.GetArrayLength()} 低于骨架 {expected.GetArrayLength()}");
                for (var i = 0; i < expected.GetArrayLength(); i++)
                {
                    AssertShape(expected[i], actual[i], $"{path}[{i}]");
                }
                break;

            case JsonValueKind.Null:
                Assert.True(actual.ValueKind == JsonValueKind.Null, $"{path}: 期望 null");
                break;

            default:
                if (expected.ValueKind == JsonValueKind.String && expected.GetString() == "<dynamic>")
                {
                    Assert.True(actual.ValueKind is JsonValueKind.String or JsonValueKind.Number,
                        $"{path}: <dynamic> 占位期望字符串/数字，实际 {actual.ValueKind}");
                }
                else
                {
                    Assert.Equal(expected.GetRawText(), actual.GetRawText());
                }
                break;
        }
    }

    // ---------- 契约用例 ----------

    [Fact]
    public async Task Authenticate_InvalidCredentials_MatchesGoldenErrorBytes()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = account.Email,
            password = "wrong-password",
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "authenticate_invalid_credentials.json");
    }

    [Fact]
    public async Task Authenticate_BannedWithCorrectPassword_MatchesGoldenBanReveal()
    {
        // V5 §6.5 评审修订：封禁详情仅向密码持有者披露——密码正确才揭示，密码错误落统一
        // 话术（见上一用例）；永久封禁 + 固定理由使 golden 文案无时间分量，逐字节恒定。
        // 种子只改 user 行，走 PERM 真实回源路径（cache miss → 读行构建快照）。
        var client = CreateClient();
        var account = await SeedAccountAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == account.Email);
            user.IsBanned = true;
            user.BannedReason = "违规内容测试";
            user.BannedUntil = null;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = account.Email,
            password = account.Password,
            agent = new { name = "Minecraft", version = 1 },
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "authenticate_banned.json");
    }

    [Fact]
    public async Task Authenticate_MalformedBody_MatchesGoldenErrorBytes()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "malformed_request.json");
    }

    [Fact]
    public async Task Authenticate_NullPasswordField_RejectedAsMalformed()
    {
        // H2 回归：required 对显式 null 不拦截（STJ 穿透实证）——若下沉 handler 会
        // 抛 ArgumentNullException → 非协议 500；端点字段守卫须回 400 malformed。
        var client = CreateClient();

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = "victim@example.com",
            password = (string?)null,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "malformed_request.json");
    }

    [Fact]
    public async Task Authenticate_NullUsernameField_RejectedAsMalformed()
    {
        // 同 H2：username:null 此前会写 FAIL_ACCT: 空后缀键（键空间污染）。
        var client = CreateClient();

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = (string?)null,
            password = "whatever-123",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "malformed_request.json");
    }

    [Fact]
    public async Task Authenticate_Success_MatchesResponseShape()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();

        var response = await client.PostAsync("/api/yggdrasil/authserver/authenticate", Json(new
        {
            username = account.Email,
            password = account.Password,
            agent = new { name = "Minecraft", version = 1 },
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMatchesShapeAsync(response, "authenticate_success.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.False(string.IsNullOrEmpty(root.GetProperty("accessToken").GetString()));
        Assert.Equal(account.PlayerUuid, root.GetProperty("selectedProfile").GetProperty("id").GetString());
        Assert.Equal(account.PlayerName, root.GetProperty("selectedProfile").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Validate_Success_ReturnsFixtureStatusEmptyBody()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();
        using var auth = await AuthenticateAsync(client, account);
        var accessToken = auth.RootElement.GetProperty("accessToken").GetString()!;

        var response = await client.PostAsync("/api/yggdrasil/authserver/validate",
            Json(new { accessToken }));

        await AssertEmptyWithFixtureStatusAsync(response, "validate_success_204.json");
    }

    [Fact]
    public async Task Refresh_Success_MatchesResponseShape()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();
        using var auth = await AuthenticateAsync(client, account);
        var accessToken = auth.RootElement.GetProperty("accessToken").GetString()!;
        var clientToken = auth.RootElement.GetProperty("clientToken").GetString()!;

        var response = await client.PostAsync("/api/yggdrasil/authserver/refresh",
            Json(new { accessToken, clientToken }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMatchesShapeAsync(response, "refresh_success.json");

        using var refreshed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(refreshed.RootElement.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Invalidate_Success_ReturnsFixtureStatusEmptyBody()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();
        using var auth = await AuthenticateAsync(client, account);
        var accessToken = auth.RootElement.GetProperty("accessToken").GetString()!;

        var response = await client.PostAsync("/api/yggdrasil/authserver/invalidate",
            Json(new { accessToken }));

        await AssertEmptyWithFixtureStatusAsync(response, "invalidate_success_204.json");
    }

    [Fact]
    public async Task Signout_Success_ReturnsFixtureStatusEmptyBody()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();

        var response = await client.PostAsync("/api/yggdrasil/authserver/signout", Json(new
        {
            username = account.Email,
            password = account.Password,
        }));

        await AssertEmptyWithFixtureStatusAsync(response, "signout_success_204.json");
    }

    [Fact]
    public async Task Signout_NullPasswordField_RejectedAsMalformed()
    {
        // H2 回归：signout 与 authenticate 同缺口——字段级 null 须 400 malformed 而非 500
        var client = CreateClient();

        var response = await client.PostAsync("/api/yggdrasil/authserver/signout", Json(new
        {
            username = "victim@example.com",
            password = (string?)null,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMatchesGoldenAsync(response, "malformed_request.json");
    }

    [Fact]
    public async Task Join_ThenHasJoined_MatchesResponseShape()
    {
        var client = CreateClient();
        var account = await SeedAccountAsync();
        using var auth = await AuthenticateAsync(client, account);
        var accessToken = auth.RootElement.GetProperty("accessToken").GetString()!;
        var serverId = Guid.NewGuid().ToString("N");

        var join = await client.PostAsync("/api/yggdrasil/sessionserver/session/minecraft/join", Json(new
        {
            accessToken,
            selectedProfile = new { id = account.PlayerUuid, name = account.PlayerName },
            serverId,
        }));
        Assert.Equal(HttpStatusCode.NoContent, join.StatusCode);

        var response = await client.GetAsync(
            $"/api/yggdrasil/sessionserver/session/minecraft/hasJoined?username={account.PlayerName}&serverId={serverId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMatchesShapeAsync(response, "hasjoined_success.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(account.PlayerUuid, doc.RootElement.GetProperty("id").GetString());
        Assert.Equal(account.PlayerName, doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task HasJoined_Missing_ReturnsFixtureStatusEmptyBody()
    {
        var client = CreateClient();

        var response = await client.GetAsync(
            $"/api/yggdrasil/sessionserver/session/minecraft/hasJoined?username=Ghost&serverId={Guid.NewGuid():N}");

        // 与实现一致（T3-010 修正原 fixture 的 403 + "Player not found" 矛盾）：未消费到票据 → 204 无响应体
        await AssertEmptyWithFixtureStatusAsync(response, "hasjoined_missing_player.json");
    }

    [Fact]
    public async Task Metadata_MatchesAuthlibShape()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/yggdrasil");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMatchesShapeAsync(response, "metadata_response.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var meta = doc.RootElement.GetProperty("meta");
        Assert.Equal("FantasyTown", meta.GetProperty("serverName").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("signaturePublickey").GetString()));
    }
}
