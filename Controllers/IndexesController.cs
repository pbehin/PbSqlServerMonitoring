using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Missing indexes analysis endpoints with pagination and caching.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class IndexesController : ControllerBase
{
    private readonly MissingIndexService _indexService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IndexesController> _logger;

    public IndexesController(
        MissingIndexService indexService, 
        IMemoryCache cache,
        ILogger<IndexesController> logger)
    {
        _indexService = indexService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get missing index recommendations with pagination.
    /// Results are cached for 5 minutes as index recommendations don't change frequently.
    /// </summary>
    [HttpGet("missing")]
    public async Task<IActionResult> GetMissingIndexes(
        [FromQuery] int top = MetricsConstants.DefaultTopN,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MetricsConstants.DefaultSmallPageSize)
    {
        try
        {
            top = PaginationExtensions.ValidateTopN(top);
            (page, pageSize) = PaginationExtensions.ValidatePagination(page, pageSize, MetricsConstants.MaxSmallPageSize);
            
            // Try to get from cache first
            var cacheKey = $"{MetricsConstants.MissingIndexesCacheKey}_{top}";
            
            if (!_cache.TryGetValue(cacheKey, out List<MissingIndex>? results))
            {
                results = await _indexService.GetMissingIndexesAsync(top);
                
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
            return StatusCode(500, new { error = "Unexpected error while fetching missing indexes" });
        }
    }
    
    /// <summary>
    /// Invalidates the missing indexes cache.
    /// Use this after making index changes to see fresh recommendations.
    /// </summary>
    [HttpPost("missing/refresh")]
    public IActionResult RefreshCache()
    {
        // Remove all cached entries with the prefix
        for (int i = 1; i <= MetricsConstants.MaxTopN; i++)
        {
            _cache.Remove($"{MetricsConstants.MissingIndexesCacheKey}_{i}");
        }
        
        _logger.LogInformation("Missing indexes cache invalidated by user");
        return Ok(new { success = true, message = "Cache cleared. Next request will fetch fresh data." });
    }
}
