using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Lock analysis endpoints with pagination.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class LocksController : ControllerBase
{
    private readonly BlockingService _blockingService;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ILogger<LocksController> _logger;

    public LocksController(
        BlockingService blockingService, 
        MultiConnectionService multiConnectionService,
        ILogger<LocksController> logger)
    {
        _blockingService = blockingService;
        _multiConnectionService = multiConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Get current locks in the system with pagination
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentLocks(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            
            var results = await _blockingService.GetCurrentLocksAsync(connStr);
            
            var totalCount = results.Count;
            var pagedResults = results
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            
            return Ok(new PagedResult<object>
            {
                Items = pagedResults.Cast<object>().ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch current locks");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching current locks"));
        }
    }
}
