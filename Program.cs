using System.Net;
using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Sessions;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. 基础设施
// T3-005：基础配置不携带可用凭据，通过环境变量注入（V5 §10：Secret → 容器/环境变量）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. " +
        "Inject credentials via environment variable ConnectionStrings__DefaultConnection (V5 §10).");
}
builder.Services.AddDbContextPool<AuthDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Redis 连接配置 — 直接构建 ConfigurationOptions，避免字符串解析问题
var redisHost = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
var redisPassword = builder.Configuration["Redis:Password"];

var redisConfig = new ConfigurationOptions
{
    AbortOnConnectFail = false,
    Password = redisPassword ?? ""
};
redisConfig.EndPoints.Add(redisHost);

if (!string.IsNullOrEmpty(redisPassword))
    Console.WriteLine($"[Redis] Connecting with password (length={redisPassword.Length})");
else
    Console.WriteLine("[Redis] WARNING: No password configured! Inject Redis__Password via environment in production (V5 §10).");

var redisConnection = ConnectionMultiplexer.Connect(redisConfig);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// DataProtection 密钥 Redis 化（V5 §9.6/§10：多实例共享；重新部署不丢已签发限时令牌）
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redisConnection, "DATA_PROTECT:keys")
    .SetApplicationName("FantasyTown.Auth");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>()
    .AddRedis(redisConfig.ToString());

// 2. OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FantasyTown.Auth"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("FantasyTown.Auth");
    });

// 3. 认证服务
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-ft_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

// 4. 服务注册
builder.Services.AddScoped<IPasswordService, Argon2PasswordService>();
builder.Services.AddScoped<IPermissionSnapshot, RedisPermissionSnapshot>();
builder.Services.AddScoped<ILockoutService, RedisLockoutService>();
// 站点选项（V5 配置表 options 项；DB 表热加载随 options 轨道接入）
// 绑定自 SiteOptions 配置节（appsettings / 环境变量），空节回退默认值
builder.Services.AddSingleton(builder.Configuration.GetSection("SiteOptions").Get<SiteOptions>() ?? new SiteOptions());
// 注册配额（V5 §3.4 REG_IP:{ip}，软配额前置粗检 + 成功后计数）
builder.Services.AddScoped<IRegistrationQuotaService>(sp =>
    new RedisRegistrationQuotaService(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<SiteOptions>(),
        sp.GetRequiredService<ILogger<RedisRegistrationQuotaService>>()));

// 4.1 反代信任与客户端 IP（V5 §8：仅信任显式配置的 KnownProxies；未配置时保持直连 RemoteIpAddress 语义）
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClientIp, HttpContextClientIp>();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// 清空框架默认信任（回环），改为纯显式白名单
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
{
    if (IPAddress.TryParse(proxy, out var ip))
    {
        forwardedHeadersOptions.KnownProxies.Add(ip);
    }
}
builder.Services.AddSingleton(Options.Create(forwardedHeadersOptions));

// 5. Yggdrasil 服务
var yggOptions = builder.Configuration.GetSection("Yggdrasil").Get<YggOptions>() ?? new YggOptions();
builder.Services.AddSingleton(yggOptions);
builder.Services.AddSingleton<AuthConcurrencyLimiter>();
builder.Services.AddSingleton<IRateLimiter>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new IpRateLimiter(redis, yggOptions.AuthRateLimitPerIp);
});
builder.Services.AddScoped<AuthenticateHandler>();
builder.Services.AddScoped<ValidateHandler>();
builder.Services.AddScoped<RefreshHandler>();
builder.Services.AddScoped<InvalidateHandler>();
builder.Services.AddScoped<SignoutHandler>();
builder.Services.AddScoped<ITokenService>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new RedisTokenService(redis, yggOptions.TokenExpire2);
});

// 5.1 Yggdrasil Sessionserver 服务
builder.Services.AddScoped<ITicketService>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new RedisTicketService(redis);
});
builder.Services.AddScoped<IPlayerCache>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new RedisPlayerCache(redis);
});
builder.Services.AddSingleton<ISigningService>(sp =>
{
    var keyPath = builder.Configuration["Signing:PrivateKeyPath"] ?? "keys/signing.pem";
    return new RsaSigningService(keyPath);
});
builder.Services.AddScoped<JoinHandler>();
builder.Services.AddScoped<HasJoinedHandler>();
builder.Services.AddScoped<ProfileHandler>();

// 6. Razor Pages
builder.Services.AddRazorPages();

var app = builder.Build();

// 数据库自动检测与初始化
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // 检测数据库是否存在
        var canConnect = await dbContext.Database.CanConnectAsync();

        if (!canConnect)
        {
            logger.LogWarning("Database not found, attempting to create and apply migrations...");

            // 应用迁移创建数据库
            await dbContext.Database.MigrateAsync();

            logger.LogInformation("Database created and migrations applied successfully.");
        }
        else
        {
            // 数据库存在，检查是否有待应用的迁移
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            var pendingMigrationsList = pendingMigrations.ToList();

            if (pendingMigrationsList.Count > 0)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Found {Count} pending migrations: {Migrations}",
                        pendingMigrationsList.Count,
                        string.Join(", ", pendingMigrationsList));
                }

                // 自动应用待处理的迁移
                await dbContext.Database.MigrateAsync();

                logger.LogInformation("Pending migrations applied successfully.");
            }
            else
            {
                logger.LogInformation("Database is up to date. No pending migrations.");
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing the database");

        // 生产环境：记录错误但不阻止应用启动（由运行时决定是否继续）
        // 开发环境：重新抛出以便调试
        if (app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// 0. 反代头解析（V5 §8：必须位于管道最前端，此后 RemoteIpAddress 即为受信客户端 IP）
app.UseForwardedHeaders();

// 中间件管道
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// 纹理端点必须在 HTTPS 重定向之前注册，Minecraft 客户端不跟随重定向
app.MapTextures();

// 认证和授权中间件（必须在路由之后）
app.UseAuthentication();
app.UseAuthorization();

// 封禁用户拦截中间件
app.UseMiddleware<RejectBannedUserMiddleware>();

app.MapRazorPages();
app.MapHealthChecks("/health");

// authlib-injector 元数据端点（必须先于其他路由）
var signingService = app.Services.GetRequiredService<ISigningService>();
app.MapMetadata(signingService.GetPublicKeyPem(), yggOptions);

// Yggdrasil API 端点
app.MapAuthenticate();
app.MapValidate();
app.MapRefresh();
app.MapInvalidate();
app.MapSignout();

// Yggdrasil Sessionserver 端点
app.MapJoin();
app.MapHasJoined();
app.MapProfile();

app.Run();

// 使 WebApplicationFactory 可访问
public partial class Program { }
