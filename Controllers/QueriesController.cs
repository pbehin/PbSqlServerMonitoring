using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Query performance monitoring endpoints with pagination support.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class QueriesController : ControllerBase
{
    private readonly QueryPerformanceService _queryService;
    private readonly MetricsQueryService _metricsQueryService;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ILogger<QueriesController> _logger;

    public QueriesController(
        QueryPerformanceService queryService, 
        MetricsQueryService metricsQueryService, 
        MultiConnectionService multiConnectionService,
        ILogger<QueriesController> logger)
    {
        _queryService = queryService;
        _metricsQueryService = metricsQueryService;
        _multiConnectionService = multiConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Get top queries by CPU usage with pagination
    /// </summary>
    [HttpGet("top-cpu")]
    public async Task<IActionResult> GetTopCpuQueries(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetTopCpuQueriesAsync(top, connStr, includeExecutionPlan: false);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top CPU queries");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching top CPU queries"));
        }
    }

    /// <summary>
    /// Get currently active queries by CPU usage (real-time)
    /// </summary>
    [HttpGet("active-cpu")]
    public async Task<IActionResult> GetActiveCpuQueries(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] int top = 5)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            top = PaginationExtensions.ValidateTopN(top, 20); // Smaller max for active queries
            var results = await _queryService.GetActiveHighCpuQueriesAsync(top, connStr, includeExecutionPlan: false);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch active CPU queries");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching active CPU queries"));
        }
    }

    /// <summary>
    /// Get history summary of top queries with pagination
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetQueryHistory(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] double hours = MetricsConstants.DefaultQueryHistoryHours, 
        [FromQuery] string sortBy = "cpu",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));

            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _metricsQueryService.GetQueryHistorySummaryAsync(hours, sanitizedId!, sortBy);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch query history summary");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching query history"));
        }
    }

    /// <summary>
    /// Get top queries by IO (logical reads) with pagination
    /// </summary>
    [HttpGet("top-io")]
    public async Task<IActionResult> GetTopIoQueries(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetTopIoQueriesAsync(top, connStr, includeExecutionPlan: false);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top IO queries");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching top IO queries"));
        }
    }

    /// <summary>
    /// Get slowest queries by elapsed time with pagination
    /// </summary>
    [HttpGet("slowest")]
    public async Task<IActionResult> GetSlowestQueries(
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            var connStr = _multiConnectionService.GetConnectionString(sanitizedId!);
            if (string.IsNullOrEmpty(connStr)) return BadRequest(ApiResponse.Error("Connection not found"));

            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            var results = await _queryService.GetSlowestQueriesAsync(top, connStr, includeExecutionPlan: false);
            var pagedResult = results.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch slowest queries");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching slowest queries"));
        }
    }

    /// <summary>
    /// Get execution plan for a specific query
    /// </summary>
    [HttpGet("plan/{queryHash}")]
    public async Task<IActionResult> GetExecutionPlan(
        [FromRoute] string queryHash,
        [FromHeader(Name = "X-Connection-Id")] string? connectionId)
    {
        try
        {
            var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
            if (!isValid) return BadRequest(ApiResponse.Error(error!));
            
            if (string.IsNullOrEmpty(queryHash)) return BadRequest(ApiResponse.Error("Query hash is required"));

            var plan = await _metricsQueryService.GetExecutionPlanAsync(sanitizedId!, queryHash);
            
            if (string.IsNullOrEmpty(plan))
            {
                return NotFound(ApiResponse.Error("Execution plan not found"));
            }
            
            return Ok(new { ExecutionPlan = plan });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch execution plan for query {QueryHash}", queryHash);
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching execution plan"));
        }
    }
}
