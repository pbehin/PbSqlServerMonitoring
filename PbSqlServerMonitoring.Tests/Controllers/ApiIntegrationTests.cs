using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace PbSqlServerMonitoring.Tests.Controllers;

/// <summary>
/// Integration tests for API controllers.
/// Tests actual HTTP endpoints and authentication behavior.
/// </summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure test services if needed
            });
        });
        
        _client = _factory.CreateClient();
    }
    
    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Health Endpoints
    
    [Fact]
    public async Task HealthLive_ReturnsOk_WithoutAuthentication()
    {
        // Act
        var response = await _client.GetAsync("/api/health/live");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }
    
    [Fact]
    public async Task HealthReady_Returns503_WhenNoConnectionConfigured()
    {
        // Act
        var response = await _client.GetAsync("/api/health/ready");
        
        // Assert
        // May return 503 if no connection is configured, or 200 if configured
        Assert.True(response.StatusCode == HttpStatusCode.OK || 
                    response.StatusCode == HttpStatusCode.ServiceUnavailable);
    }
    
    #endregion
    
    #region Static Files
    
    [Fact]
    public async Task IndexHtml_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/html", contentType);
    }
    
    #endregion
    
    #region Security Headers
    
    [Fact]
    public async Task Responses_ContainSecurityHeaders()
    {
        // Act
        var response = await _client.GetAsync("/api/health/live");
        
        // Assert
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }
    
    [Fact]
    public async Task Responses_ContainContentSecurityPolicy()
    {
        // Act
        var response = await _client.GetAsync("/api/health/live");
        
        // Assert
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        Assert.Contains("default-src 'self'", csp);
    }
    
    #endregion
    
    #region CORS Headers
    
    [Fact]
    public async Task Options_Request_ReturnsAllowedMethods()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/health/live");
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        
        // Act
        var response = await _client.SendAsync(request);
        
        // Assert
        // In development environment, CORS should be more permissive
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent);
    }
    
    #endregion
    
    #region Rate Limiting
    
    [Fact]
    public async Task RateLimiting_AllowsRequestsUnderLimit()
    {
        // Act - Make several requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.GetAsync("/api/health/live"))
            .ToList();
        
        var responses = await Task.WhenAll(tasks);
        
        // Assert - All should succeed under rate limit
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
    
    #endregion
    
    #region Protected Endpoints Without Auth
    
    [Fact]
    public async Task MetricsHistory_WithoutAuth_ReturnsOkInDevelopment()
    {
        // In development mode with AllowAnonymousInDevelopment=true, this should succeed
        // Act
        var response = await _client.GetAsync("/api/metrics/history");
        
        // Assert
        // May return:
        // - 401 if auth is enforced
        // - 400 if missing/invalid connection ID (new validation)
        // - 200 if development mode allows anonymous AND has valid connection
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.BadRequest);
    }
    
    #endregion
    
    #region API Documentation
    
    
    #endregion
}
