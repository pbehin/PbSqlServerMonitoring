using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;

namespace PbSqlServerMonitoring.Security;

/// <summary>
/// API Key authentication handler.
/// 
/// Security Improvements:
/// - API key read from environment variable only (never from config file)
/// - Only accepts API key from header (removed query string support for security)
/// - Logs failed authentication attempts with IP
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string ApiKeyHeaderName = "X-API-Key";
    private const string ApiKeyEnvVarConfigKey = "Security:ApiKeyEnvironmentVariable";
    private const string DefaultApiKeyEnvVar = "PB_MONITOR_API_KEY";
    
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Allow anonymous access in Development mode if configured
        var environment = Context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var allowAnonymousInDev = _configuration.GetValue<bool>("Security:AllowAnonymousInDevelopment", true);
        
        if (environment.IsDevelopment() && allowAnonymousInDev)
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("Developer")));
        }

        // Check if authentication is disabled globally
        var authEnabled = _configuration.GetValue<bool>("Security:EnableAuthentication", false);
        if (!authEnabled)
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("Anonymous")));
        }

        // Only accept API key from header (not query string for security)
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key is missing. Provide X-API-Key header."));
        }

        var providedApiKey = apiKeyHeader.ToString();

        // Get API key from environment variable (never from config file)
        var envVarName = _configuration[ApiKeyEnvVarConfigKey] ?? DefaultApiKeyEnvVar;
        var validApiKey = Environment.GetEnvironmentVariable(envVarName);

        if (string.IsNullOrEmpty(validApiKey))
        {
            _logger.LogWarning(
                "No API key configured. Set environment variable '{EnvVar}' to enable authentication",
                envVarName);
            return Task.FromResult(AuthenticateResult.Fail("Server configuration error: API key not configured"));
        }

        // Use constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedApiKey),
            System.Text.Encoding.UTF8.GetBytes(validApiKey)))
        {
            _logger.LogWarning(
                "Invalid API key attempt from {RemoteIp}", 
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        return Task.FromResult(AuthenticateResult.Success(CreateTicket("ApiKeyUser")));
    }

    private AuthenticationTicket CreateTicket(string username)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, username), // Required for user-specific connection filtering
            new Claim(ClaimTypes.AuthenticationMethod, "ApiKey")
        };
        
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}

/// <summary>
/// Options for API Key authentication.
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Extension methods for adding API Key authentication.
/// </summary>
public static class ApiKeyAuthenticationExtensions
{
    public const string SchemeName = "ApiKey";

    public static AuthenticationBuilder AddApiKeyAuthentication(
        this AuthenticationBuilder builder,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null)
    {
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            SchemeName,
            configureOptions ?? (_ => { }));
    }
}
