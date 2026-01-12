using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Blocking sessions monitoring endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BlockingController : ControllerBase
{
    private readonly BlockingService _blockingService;
    private readonly ILogger<BlockingController> _logger;

    public BlockingController(BlockingService blockingService, ILogger<BlockingController> logger)
    {
        _blockingService = blockingService;
        _logger = logger;
    }

    /// <summary>
    /// Get currently blocking sessions
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetBlockingSessions()
    {
        try
        {
            var results = await _blockingService.GetBlockingSessionsAsync();
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch blocking sessions");
            return StatusCode(500, new { error = "Unexpected error while fetching blocking sessions" });
        }
    }
}
