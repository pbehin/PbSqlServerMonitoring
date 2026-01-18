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

var keyRingPath = builder.Configuration["KeyRingPath"]
    ?? Environment.GetEnvironmentVariable("PB_MONITOR_KEYRING_PATH");

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
        .SetApplicationName("PbSqlServerMonitoring");
}
catch (Exception ex)
{
    throw new InvalidOperationException($"Failed to configure key ring path '{keyRingPath}'. Check path and permissions.", ex);
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;

    options.User.RequireUniqueEmail = true;

    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<MonitorDbContext>()
.AddDefaultTokenProviders();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
    .AddApiKeyAuthentication();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/api/auth/callback";
    });
}

builder.Services.AddAuthorization();

var resetTokenMinutes = builder.Configuration.GetValue<int?>("PasswordReset:TokenLifetimeMinutes") ?? 5;
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromMinutes(resetTokenMinutes);
});

builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

var rateLimitPermits = builder.Configuration.GetValue("RateLimiting:PermitLimit", MetricsConstants.RateLimitPerMinute);
var rateLimitWindow = builder.Configuration.GetValue("RateLimiting:WindowSeconds", MetricsConstants.RateLimitWindowSeconds);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        var partitionKey = !string.IsNullOrEmpty(apiKey)
            ? $"key:{apiKey[..Math.Min(8, apiKey.Length)]}"
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

    options.AddFixedWindowLimiter("connection-test", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
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

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
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

builder.Services.AddSingleton<IUserPreferencesService, UserPreferencesService>();

builder.Services.AddSingleton<IMultiConnectionService, MultiConnectionService>();

builder.Services.AddSingleton<IPrometheusTargetExporter, PrometheusTargetExporter>();
builder.Services.AddHostedService<ConnectionHealthMonitor>();

builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundTaskQueueHostedService>();

builder.Services.AddDbContext<MonitorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PbMonitorConnection")));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Capture Grafana URL for CSP
var grafanaBaseUrl = builder.Configuration["Grafana:BaseUrl"] 
    ?? throw new InvalidOperationException("Grafana:BaseUrl is missing");

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        logger.LogInformation("Created Admin role");
    }

    if (!await roleManager.RoleExistsAsync("User"))
    {
        await roleManager.CreateAsync(new IdentityRole("User"));
        logger.LogInformation("Created User role");
    }

    var adminEmail = builder.Configuration["Monitoring:AdminEmail"];
    if (!string.IsNullOrEmpty(adminEmail))
    {
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser != null && !await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            logger.LogInformation("Promoted {Email} to Admin role", adminEmail);
        }
    }
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        $"frame-src 'self' {grafanaBaseUrl}; " +
        "connect-src 'self'");

    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");

    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
    }

    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");

    await next();
});

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SQL Server Monitoring API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();
