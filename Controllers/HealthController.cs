using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Health endpoint for server status.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ServerHealthService _healthService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(ServerHealthService healthService, ILogger<HealthController> logger)
    {
        _healthService = healthService;
        _logger = logger;
    }

    /// <summary>
    /// Get SQL Server health status
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var health = await _healthService.GetServerHealthAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve server health");
            return StatusCode(500, new { 
                isConnected = false, 
                errorMessage = "Unexpected error while retrieving server health" 
            });
        }
    }
}
