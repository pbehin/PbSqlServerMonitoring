using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Security;
using PbSqlServerMonitoring.Services;
using PbSqlServerMonitoring.Models;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// Service Configuration
// ============================================================

// Add Data Protection for secure connection string encryption
// IMPORTANT: Keys must be persisted to prevent connection string decryption failures after restart
var keyRingPath = builder.Configuration["KeyRingPath"] 
    ?? Environment.GetEnvironmentVariable("PB_MONITOR_KEYRING_PATH");

// Default to ./data/keys if no explicit path configured
if (string.IsNullOrWhiteSpace(keyRingPath))
{
    keyRingPath = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
    Console.WriteLine($"[Data Protection] Using default key path: {keyRingPath}");
}

try
{
    Directory.CreateDirectory(keyRingPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("PbSqlServerMonitoring"); // Ensures key isolation
}
catch (Exception ex)
{
    throw new InvalidOperationException($"Failed to configure key ring path '{keyRingPath}'. Check path and permissions.", ex);
}

// Add Authentication (Identity + Social)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Minimal password settings for internal tool ease of use
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    
    // User settings
    options.User.RequireUniqueEmail = true;
    
    // Require email confirmation before login
    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<MonitorDbContext>()
.AddDefaultTokenProviders();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    // Default to cookies for web browser requests
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
    .AddApiKeyAuthentication();

// Only add Google OAuth if credentials are configured
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        // Set callback path to match our custom callback route
        options.CallbackPath = "/api/auth/callback";
    });
}

builder.Services.AddAuthorization();

// Configure token lifetimes (e.g., password reset links)
var resetTokenMinutes = builder.Configuration.GetValue<int?>("PasswordReset:TokenLifetimeMinutes") ?? 5;
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromMinutes(resetTokenMinutes);
});

// Add Email Service
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

// Add Rate Limiting
var rateLimitPermits = builder.Configuration.GetValue("RateLimiting:PermitLimit", MetricsConstants.RateLimitPerMinute);
var rateLimitWindow = builder.Configuration.GetValue("RateLimiting:WindowSeconds", MetricsConstants.RateLimitWindowSeconds);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Partition rate limits by API key (if present) or IP address
    // This provides per-user rate limiting for authenticated requests
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Check for API key first for authenticated rate limiting
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        var partitionKey = !string.IsNullOrEmpty(apiKey) 
            ? $"key:{apiKey[..Math.Min(8, apiKey.Length)]}" // Use first 8 chars of API key for partitioning (privacy)
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermits,
                Window = TimeSpan.FromSeconds(rateLimitWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });
    
    // Stricter rate limit for connection testing (prevent brute-force attacks)
    options.AddFixedWindowLimiter("connection-test", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5; // Max 5 tests per minute
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2;
    });
    
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
builder.Services.AddSingleton<UserPreferencesService>();

// Register multi-connection service for managing multiple SQL Server connections
// Uses IDbContextFactory for database access, allowing singleton registration
builder.Services.AddSingleton<MultiConnectionService>();
builder.Services.AddSingleton<IMultiConnectionService>(sp => sp.GetRequiredService<MultiConnectionService>());

// Register background task queue for proper async task management
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundTaskQueueHostedService>();

// Register EF Core Context (required by ASP.NET Identity and used via IServiceScopeFactory by singleton services)
builder.Services.AddDbContext<MonitorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PbMonitorConnection")));

// Register metrics collection as singleton and hosted service for background collection
builder.Services.AddSingleton<MetricsCollectionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsCollectionService>());

// Register connection health monitor to continuously test all configured connections
builder.Services.AddHostedService<ConnectionHealthMonitor>();

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

// Force HTTPS redirection
app.UseHttpsRedirection();

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
    
    // HSTS - Enforce HTTPS (only in production to avoid development issues)
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
    }
    
    // Permissions Policy - restrict browser features
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    
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
