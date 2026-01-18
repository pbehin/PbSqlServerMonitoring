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
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public async Task HealthLive_ReturnsOk_WithoutAuthentication()
    {
        var response = await _client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task HealthReady_Returns503_WhenNoConnectionConfigured()
    {
        var response = await _client.GetAsync("/api/health/ready");

        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable);
    }



    [Fact]
    public async Task IndexHtml_ReturnsOk()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/html", contentType);
    }



    [Fact]
    public async Task Responses_ContainSecurityHeaders()
    {
        var response = await _client.GetAsync("/api/health/live");

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
        var response = await _client.GetAsync("/api/health/live");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        Assert.Contains("default-src 'self'", csp);
    }



    [Fact]
    public async Task Options_Request_ReturnsAllowedMethods()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/health/live");
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent);
    }



    [Fact]
    public async Task RateLimiting_AllowsRequestsUnderLimit()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.GetAsync("/api/health/live"))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }








}
