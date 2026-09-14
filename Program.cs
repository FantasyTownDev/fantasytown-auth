using FantasyTown.Auth.Persistence;
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

// 3. Razor Pages
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
            
            if (pendingMigrationsList.Any())
            {
                logger.LogInformation("Found {Count} pending migrations: {Migrations}", 
                    pendingMigrationsList.Count, 
                    string.Join(", ", pendingMigrationsList));
                
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
app.UseAuthorization();

app.MapRazorPages();
app.MapHealthChecks("/health");

app.Run();
