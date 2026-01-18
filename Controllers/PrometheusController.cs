using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for Prometheus integration.
///
/// Provides:
/// - HTTP Service Discovery endpoint for Prometheus to discover SQL Server targets
/// - Connection string endpoint for sql_exporter to fetch credentials
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PrometheusController : ControllerBase
{
    private readonly ILogger<PrometheusController> _logger;

    public PrometheusController(ILogger<PrometheusController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Health check for Prometheus to verify the service is running.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
