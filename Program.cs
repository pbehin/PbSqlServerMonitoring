using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Security;
using PbSqlServerMonitoring.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// Service Configuration
// ============================================================

// Add Data Protection for secure connection string encryption
var keyRingPath = builder.Configuration["KeyRingPath"] ?? Environment.GetEnvironmentVariable("PB_MONITOR_KEYRING_PATH");
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    try
    {
        Directory.CreateDirectory(keyRingPath);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to configure key ring path '{keyRingPath}'. Check path and permissions.", ex);
    }
}
else
{
    builder.Services.AddDataProtection();
}

// Add Authentication
builder.Services.AddAuthentication(ApiKeyAuthenticationExtensions.SchemeName)
    .AddApiKeyAuthentication();
builder.Services.AddAuthorization();

// Add Rate Limiting
var rateLimitPermits = builder.Configuration.GetValue("RateLimiting:PermitLimit", MetricsConstants.RateLimitPerMinute);
var rateLimitWindow = builder.Configuration.GetValue("RateLimiting:WindowSeconds", MetricsConstants.RateLimitWindowSeconds);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermits,
                Window = TimeSpan.FromSeconds(rateLimitWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            }));
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        
        var response = new { error = "Too many requests. Please try again later." };
        await context.HttpContext.Response.WriteAsJsonAsync(response, token);
    };
});

// Add Memory Cache for expensive queries
builder.Services.AddMemoryCache();

// Add controllers with JSON options
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() 
    { 
        Title = "SQL Server Monitoring API", 
        Version = "v1",
        Description = "API for monitoring SQL Server performance. Use X-API-Key header for authentication in production."
    });
});

// Register monitoring services as singletons (shared state for connection)
builder.Services.AddSingleton<ConnectionService>();
builder.Services.AddSingleton<QueryPerformanceService>();
builder.Services.AddSingleton<MissingIndexService>();
builder.Services.AddSingleton<BlockingService>();
builder.Services.AddSingleton<ServerHealthService>();
builder.Services.AddSingleton<RunningQueriesService>();
builder.Services.AddSingleton<IMetricsPersistenceService, SqlPersistenceService>();

// Register buffer management and query services
builder.Services.AddSingleton<MetricsBufferService>();
builder.Services.AddSingleton<MetricsQueryService>();

// Register background task queue for proper async task management
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundTaskQueueHostedService>();

// Register EF Core Context
builder.Services.AddDbContext<MonitorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PbMonitorConnection")));

// Register metrics collection as singleton and hosted service for background collection
builder.Services.AddSingleton<MetricsCollectionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsCollectionService>());


// Add CORS - restricted in production
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: Allow any origin for easier testing
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Production: Restrict to configured origins
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? new[] { "https://localhost" };
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// ============================================================
// Middleware Pipeline
// ============================================================

// Security headers for all responses
app.Use(async (context, next) =>
{
    // Content Security Policy - restrict script sources
    context.Response.Headers.Append("Content-Security-Policy", 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'");
    
    // Prevent MIME type sniffing
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    
    // Clickjacking protection
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    
    // XSS Protection (legacy browsers)
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    
    // Referrer policy
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    
    await next();
});

// Rate limiting
app.UseRateLimiter();

// Swagger UI - Development only for security
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SQL Server Monitoring API v1");
        options.RoutePrefix = "swagger";
    });
}

// Standard middleware
app.UseCors();
app.UseStaticFiles();
app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Serve index.html for root path
app.MapFallbackToFile("index.html");

app.Run();
