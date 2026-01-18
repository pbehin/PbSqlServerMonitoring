using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for Grafana dashboard embedding.
///
/// Provides:
/// - Embed URL generation with user-specific filtering
/// - Admin users see all data, regular users see only their connections
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class GrafanaController : ControllerBase
{
    private readonly IMultiConnectionService _connectionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrafanaController> _logger;

    public GrafanaController(
        IMultiConnectionService connectionService,
        IConfiguration configuration,
        ILogger<GrafanaController> logger)
    {
        _connectionService = connectionService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the Grafana dashboard embed URL for the current user.
    ///
    /// - Regular users: URL includes user_id filter (see only their connections)
    /// - Admins: URL has no filter (see all connections)
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(GrafanaEmbedResponse), StatusCodes.Status200OK)]
    public IActionResult GetDashboardUrl()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var isAdmin = User.IsInRole("Admin");

        var baseUrl = _configuration["Grafana:BaseUrl"] 
            ?? throw new InvalidOperationException("Grafana:BaseUrl configuration is missing");
        var dashboardUid = _configuration["Grafana:DashboardUid"] ?? "sql-server-monitoring";
        var theme = _configuration["Grafana:Theme"] ?? "dark";

        var embedUrl = $"{baseUrl}/d/{dashboardUid}/sql-server-monitoring";

        var parameters = new List<string>
        {
            "kiosk",
            $"theme={theme}",
            "refresh=30s"
        };

        if (!isAdmin)
        {
            parameters.Add($"var-user_id={Uri.EscapeDataString(userId)}");
        }

        embedUrl += "?" + string.Join("&", parameters);

        _logger.LogDebug("Generated Grafana embed URL for user {UserId}, isAdmin={IsAdmin}", userId, isAdmin);

        return Ok(new GrafanaEmbedResponse
        {
            EmbedUrl = embedUrl,
            UserId = userId,
            IsAdmin = isAdmin,
            DashboardUid = dashboardUid,
            BaseUrl = baseUrl
        });
    }

    /// <summary>
    /// Gets available connection filters for the current user.
    /// Used to populate dropdown filters in the embedded dashboard.
    /// </summary>
    [HttpGet("connections")]
    [ProducesResponseType(typeof(List<GrafanaConnectionOption>), StatusCodes.Status200OK)]
    public IActionResult GetConnectionOptions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");

        var connections = _connectionService.GetEnabledConnections();

        if (!isAdmin)
        {
            connections = connections.Where(c => c.UserId == userId).ToList().AsReadOnly();
        }

        var options = connections.Select(c => new GrafanaConnectionOption
        {
            ConnectionId = c.Id,
            Name = c.Name,
            Server = c.Server,
            Database = c.Database,
            UserId = c.UserId ?? "unknown"
        }).ToList();

        return Ok(options);
    }
}

public sealed class GrafanaEmbedResponse
{
    public string EmbedUrl { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string DashboardUid { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class GrafanaConnectionOption
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}
