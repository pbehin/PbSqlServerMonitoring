using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Health endpoints for server status and container orchestration.
/// Includes liveness/readiness probes for Kubernetes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly ServerHealthService _healthService;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        ServerHealthService healthService, 
        MultiConnectionService multiConnectionService,
        ILogger<HealthController> logger)
    {
        _healthService = healthService;
        _multiConnectionService = multiConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Get SQL Server health status (requires authorization)
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHealth(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId)
    {
        var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
        if (!isValid)
        {
            return BadRequest(ApiResponse.Error(error!));
        }

        var connectionString = _multiConnectionService.GetConnectionString(sanitizedId!);
        if (string.IsNullOrEmpty(connectionString))
        {
            return BadRequest(ApiResponse.Error("Connection not found"));
        }

        try
        {
            var health = await _healthService.GetServerHealthAsync(connectionString);
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve server health for connection {ConnectionId}", sanitizedId);
            return StatusCode(500, ApiResponse.Error("Unexpected error while retrieving server health"));
        }
    }
    
    /// <summary>
    /// Liveness probe for container orchestration.
    /// Returns 200 if the application is running (even if SQL Server is unreachable).
    /// Used by Kubernetes to determine if the container should be restarted.
    /// </summary>
    [HttpGet("live")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult LivenessProbe()
    {
        return Ok(new 
        { 
            status = "Healthy",
            timestamp = DateTime.UtcNow
        });
    }
    
    /// <summary>
    /// Readiness probe for container orchestration.
    /// Returns 200 only if at least one SQL Server connection is configured and enabled.
    /// Used by Kubernetes to determine if the pod should receive traffic.
    /// </summary>
    [HttpGet("ready")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReadinessProbe()
    {
        try
        {
            var enabledConnections = _multiConnectionService.GetEnabledConnections().ToList();
            if (!enabledConnections.Any())
            {
                return StatusCode(503, new 
                { 
                    status = "Unhealthy",
                    reason = "No SQL Server connection configured",
                    timestamp = DateTime.UtcNow
                });
            }
            
            // Check the first enabled connection for health
            var firstConnection = enabledConnections.First();
            var connectionString = _multiConnectionService.GetConnectionString(firstConnection.Id);
            if (string.IsNullOrEmpty(connectionString))
            {
                return StatusCode(503, new 
                { 
                    status = "Unhealthy",
                    reason = "Cannot retrieve connection string",
                    timestamp = DateTime.UtcNow
                });
            }

            var health = await _healthService.GetServerHealthAsync(connectionString);
            
            if (!health.IsConnected)
            {
                return StatusCode(503, new 
                { 
                    status = "Unhealthy",
                    reason = health.ErrorMessage ?? "Cannot connect to SQL Server",
                    timestamp = DateTime.UtcNow
                });
            }
            
            return Ok(new 
            { 
                status = "Healthy",
                serverName = health.ServerName,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness probe failed");
            return StatusCode(503, new 
            { 
                status = "Unhealthy",
                reason = "Error checking SQL Server connection",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
