using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Query performance monitoring endpoints with pagination support.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class QueriesController : ControllerBase
{
    private readonly QueryPerformanceService _queryService;
    private readonly MetricsQueryService _metricsQueryService;
    private readonly ILogger<QueriesController> _logger;

    public QueriesController(
        QueryPerformanceService queryService, 
        MetricsQueryService metricsQueryService, 
        ILogger<QueriesController> logger)
    {
        _queryService = queryService;
        _metricsQueryService = metricsQueryService;
        _logger = logger;
    }

    /// <summary>
    /// Get top queries by CPU usage with pagination
    /// </summary>
    [HttpGet("top-cpu")]
    public async Task<IActionResult> GetTopCpuQueries(
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetTopCpuQueriesAsync(top);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top CPU queries");
            return StatusCode(500, new { error = "Unexpected error while fetching top CPU queries" });
        }
    }

    /// <summary>
    /// Get currently active queries by CPU usage (real-time)
    /// </summary>
    [HttpGet("active-cpu")]
    public async Task<IActionResult> GetActiveCpuQueries([FromQuery] int top = 5)
    {
        try
        {
            top = PaginationExtensions.ValidateTopN(top, 20); // Smaller max for active queries
            var results = await _queryService.GetActiveHighCpuQueriesAsync(top);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch active CPU queries");
            return StatusCode(500, new { error = "Unexpected error while fetching active CPU queries" });
        }
    }

    /// <summary>
    /// Get history summary of top queries with pagination
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetQueryHistory(
        [FromQuery] double hours = MetricsConstants.DefaultQueryHistoryHours, 
        [FromQuery] string sortBy = "cpu",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _metricsQueryService.GetQueryHistorySummaryAsync(hours, sortBy);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch query history summary");
            return StatusCode(500, new { error = "Unexpected error while fetching query history" });
        }
    }

    /// <summary>
    /// Get top queries by IO (logical reads) with pagination
    /// </summary>
    [HttpGet("top-io")]
    public async Task<IActionResult> GetTopIoQueries(
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetTopIoQueriesAsync(top);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top IO queries");
            return StatusCode(500, new { error = "Unexpected error while fetching top IO queries" });
        }
    }

    /// <summary>
    /// Get slowest queries by elapsed time with pagination
    /// </summary>
    [HttpGet("slowest")]
    public async Task<IActionResult> GetSlowestQueries(
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetSlowestQueriesAsync(top);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch slowest queries");
            return StatusCode(500, new { error = "Unexpected error while fetching slowest queries" });
        }
    }
}
