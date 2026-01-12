using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Blocking sessions monitoring endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class BlockingController : ControllerBase
{
    private readonly BlockingService _blockingService;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ILogger<BlockingController> _logger;

    public BlockingController(
        BlockingService blockingService, 
        MultiConnectionService multiConnectionService,
        ILogger<BlockingController> logger)
    {
        _blockingService = blockingService;
        _multiConnectionService = multiConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Get currently blocking sessions
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetBlockingSessions([FromHeader(Name = "X-Connection-Id")] string? connectionId)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            var results = await _blockingService.GetBlockingSessionsAsync(connStr);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch blocking sessions");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching blocking sessions"));
        }
    }
}
