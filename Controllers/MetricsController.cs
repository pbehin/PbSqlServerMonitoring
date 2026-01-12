using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for historical metrics data with pagination support.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly MetricsQueryService _queryService;

    public MetricsController(MetricsQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <summary>
    /// Gets historical metrics for the specified time range with optional pagination.
    /// </summary>
    /// <param name="rangeSeconds">Time range in seconds (default: 60, max: 172800 = 2 days)</param>
    /// <param name="page">Page number (1-based, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 100, max: 1000)</param>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int rangeSeconds = MetricsConstants.DefaultRangeSeconds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultPageSize)
    {
        // Validate parameters using extension methods
        rangeSeconds = PaginationExtensions.ValidateRangeSeconds(rangeSeconds);
        (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize);
        
        var pageResult = await _queryService.GetMetricsPageAsync(rangeSeconds, page, pageSize);

        return Ok(new PagedResult<object>
        {
            Items = pageResult.Items.Select(m => new
            {
                timestamp = m.Timestamp,
                cpu = m.CpuPercent,
                memory = m.MemoryMb,
                connections = m.ActiveConnections,
                blocked = m.BlockedProcesses,
                bufferHitRatio = m.BufferCacheHitRatio
            }).Cast<object>().ToList(),
            Page = pageResult.Page,
            PageSize = pageResult.PageSize,
            TotalCount = pageResult.TotalCount,
            TotalPages = pageResult.TotalPages
        });
    }

    /// <summary>
    /// Gets historical metrics for a custom date/time range with optional pagination.
    /// </summary>
    /// <param name="from">Start datetime (ISO 8601 format)</param>
    /// <param name="to">End datetime (ISO 8601 format)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Items per page</param>
    [HttpGet("history/range")]
    public async Task<IActionResult> GetHistoryByRange(
        [FromQuery] DateTime from, 
        [FromQuery] DateTime to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultPageSize)
    {
        // Convert to UTC if not already
        var fromUtc = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
        var toUtc = to.Kind == DateTimeKind.Utc ? to : to.ToUniversalTime();
        
        (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize);
        
        var pageResult = await _queryService.GetMetricsByDateRangePageAsync(fromUtc, toUtc, page, pageSize);

        return Ok(new PagedResult<object>
        {
            Items = pageResult.Items.Select(m => new
            {
                timestamp = m.Timestamp,
                cpu = m.CpuPercent,
                memory = m.MemoryMb,
                connections = m.ActiveConnections,
                blocked = m.BlockedProcesses,
                bufferHitRatio = m.BufferCacheHitRatio
            }).Cast<object>().ToList(),
            Page = pageResult.Page,
            PageSize = pageResult.PageSize,
            TotalCount = pageResult.TotalCount,
            TotalPages = pageResult.TotalPages
        });
    }

    /// <summary>
    /// Gets the latest metric data point.
    /// </summary>
    [HttpGet("latest")]
    public IActionResult GetLatest()
    {
        var latest = _queryService.GetLatest();
        
        if (latest == null)
        {
            return Ok(new { hasData = false });
        }

        return Ok(new
        {
            hasData = true,
            timestamp = latest.Timestamp,
            cpu = latest.CpuPercent,
            memory = latest.MemoryMb,
            connections = latest.ActiveConnections,
            blocked = latest.BlockedProcesses,
            bufferHitRatio = latest.BufferCacheHitRatio
        });
    }

    /// <summary>
    /// Provides buffer health info (queue sizes and drop counts) for observability.
    /// </summary>
    [HttpGet("buffer-health")]
    public IActionResult GetBufferHealth()
    {
        var health = _queryService.GetBufferHealth();
        return Ok(new
        {
            pendingQueueLength = health.PendingQueueLength,
            recentQueueLength = health.RecentQueueLength,
            droppedPendingTotal = health.DroppedPendingTotal,
            lastDropUtc = health.LastDropUtc
        });
    }

    /// <summary>
    /// Gets detailed blocking history with pagination.
    /// Only returns data points where blocking occurred.
    /// </summary>
    [HttpGet("blocking-history")]
    public async Task<IActionResult> GetBlockingHistory(
        [FromQuery] int rangeSeconds = MetricsConstants.MaxRangeSeconds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultPageSize)
    {
        (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize);
        
        var pageResult = await _queryService.GetBlockingHistoryPageAsync(rangeSeconds, page, pageSize);

        return Ok(new PagedResult<object>
        {
            Items = pageResult.Items.Select(m => new
            {
                timestamp = m.Timestamp,
                blockedQueries = m.BlockedQueries
            }).Cast<object>().ToList(),
            Page = pageResult.Page,
            PageSize = pageResult.PageSize,
            TotalCount = pageResult.TotalCount,
            TotalPages = pageResult.TotalPages
        });
    }
}
