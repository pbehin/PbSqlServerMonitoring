using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using static PbSqlServerMonitoring.Extensions.InputValidationExtensions;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Missing indexes analysis endpoints with pagination and caching.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class IndexesController : ControllerBase
{
    private readonly MissingIndexService _indexService;
    private readonly MultiConnectionService _multiConnectionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IndexesController> _logger;

    public IndexesController(
        MissingIndexService indexService, 
        MultiConnectionService multiConnectionService,
        IMemoryCache cache,
        ILogger<IndexesController> logger)
    {
        _indexService = indexService;
        _multiConnectionService = multiConnectionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get missing index recommendations with pagination.
    /// Results are cached for 5 minutes as index recommendations don't change frequently.
    /// </summary>
    [HttpGet("missing")]
    public async Task<IActionResult> GetMissingIndexes(
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
            
            // Try to get from cache first (per connection)
            var cacheKey = $"{MetricsConstants.MissingIndexesCacheKey}_{sanitizedId}_{top}";
            
            if (!_cache.TryGetValue(cacheKey, out List<MissingIndex>? results))
            {
                results = await _indexService.GetMissingIndexesAsync(top, connStr);
                
                // Cache for configured duration
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(MetricsConstants.DefaultCacheDurationMinutes));
                
                _cache.Set(cacheKey, results, cacheOptions);
                _logger.LogDebug("Cached missing indexes for {Minutes} minutes", MetricsConstants.DefaultCacheDurationMinutes);
            }
            
            var pagedResult = results!.ToPagedResult(page, pageSize, item => (object)item);
            
            return Ok(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch missing indexes");
            return StatusCode(500, ApiResponse.Error("Unexpected error while fetching missing indexes"));
        }
    }
    
    /// <summary>
    /// Invalidates the missing indexes cache.
    /// Use this after making index changes to see fresh recommendations.
    /// </summary>
    [HttpPost("missing/refresh")]
    public IActionResult RefreshCache([FromHeader(Name = "X-Connection-Id")] string? connectionId)
    {
        var (isValid, sanitizedId, error) = ValidateConnectionId(connectionId);
        if (!isValid) return BadRequest(ApiResponse.Error(error!));
        
        // Remove all cached entries for this connection
        for (int i = 1; i <= MetricsConstants.MaxTopN; i++)
        {
            _cache.Remove($"{MetricsConstants.MissingIndexesCacheKey}_{sanitizedId}_{i}");
        }
        
        _logger.LogInformation("Missing indexes cache invalidated by user for connection {ConnectionId}", sanitizedId);
        return Ok(ApiResponse.Ok("Cache cleared. Next request will fetch fresh data."));
    }
}
