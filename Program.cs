using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 1. 基础设施
builder.Services.AddDbContextPool<AuthDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

var redisConnectionString = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>()
    .AddRedis(redisConnectionString);

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

// 5. Razor Pages
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

// 中间件管道
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// 认证和授权中间件（必须在路由之后）
app.UseAuthentication();
app.UseAuthorization();

// 封禁用户拦截中间件
app.UseMiddleware<RejectBannedUserMiddleware>();

app.MapRazorPages();
app.MapHealthChecks("/health");

app.Run();
