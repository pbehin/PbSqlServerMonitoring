using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for running queries monitoring with pagination.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class RunningController : ControllerBase
{
    private readonly RunningQueriesService _service;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ILogger<RunningController> _logger;

    public RunningController(
        RunningQueriesService service, 
        MultiConnectionService multiConnectionService,
        ILogger<RunningController> logger)
    {
        _service = service;
        _multiConnectionService = multiConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all currently running queries with pagination.
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetRunningQueries(
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
            pageSize = Math.Clamp(pageSize, 1, 100);
            
            var queries = await _service.GetRunningQueriesAsync(connStr);
            
            var totalCount = queries.Count;
            var pagedResults = queries
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
            _logger.LogError(ex, "Failed to fetch running queries");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching running queries"));
        }
    }
}
