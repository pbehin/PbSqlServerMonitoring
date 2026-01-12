using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Service for querying and aggregating metrics data.
/// Combines data from buffer and persistence layer.
/// </summary>
public sealed class MetricsQueryService
{
    private readonly MetricsBufferService _bufferService;
    private readonly IMetricsPersistenceService _persistenceService;
    private readonly ConnectionService _connectionService;
    private readonly ILogger<MetricsQueryService> _logger;

    public MetricsQueryService(
        MetricsBufferService bufferService,
        IMetricsPersistenceService persistenceService,
        ConnectionService connectionService,
        ILogger<MetricsQueryService> logger)
    {
        _bufferService = bufferService;
        _persistenceService = persistenceService;
        _connectionService = connectionService;
        _logger = logger;
    }

    /// <summary>
    /// Gets metrics for the specified time range, merging buffer and persisted data.
    /// </summary>
    /// <summary>
    /// Gets metrics for the specified time range, merging buffer and persisted data.
    /// </summary>
    public async Task<IEnumerable<MetricDataPoint>> GetMetricsAsync(int rangeSeconds, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return Enumerable.Empty<MetricDataPoint>();

        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);

        // For very short ranges (1 minute or less), prefer in-memory data for speed
        if (rangeSeconds <= 60)
        {
            var memData = _bufferService.GetRecentDataPoints(connectionId, cutoff).ToList();
            if (memData.Count >= 3) return memData;
        }

        // For longer ranges, fetch from SQL storage
        var history = await _persistenceService.GetMetricsAsync(rangeSeconds, connectionId);
        
        // Merge memory data for the most recent portion (covers any unsaved data)
        var memRecent = _bufferService.GetRecentDataPoints(connectionId).ToList();
        
        if (memRecent.Any())
        {
            var lastSqlTs = history.LastOrDefault()?.Timestamp ?? DateTime.MinValue;
            var unsaved = memRecent.Where(p => p.Timestamp > lastSqlTs && p.Timestamp >= cutoff).ToList();
            if (unsaved.Any())
            {
                history.AddRange(unsaved);
            }
        }
        
        // Also include pending save queue items
        var pending = _bufferService.GetPendingDataPoints(connectionId, cutoff).ToList();
        
        if (pending.Any())
        {
            var lastTs = history.LastOrDefault()?.Timestamp ?? DateTime.MinValue;
            history.AddRange(pending.Where(p => p.Timestamp > lastTs));
        }
        
        return history.OrderBy(h => h.Timestamp);
    }

    public async Task<PagedResult<MetricDataPoint>> GetMetricsPageAsync(int rangeSeconds, int page, int pageSize, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return EmptyPage(page, pageSize);
        }

        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        var skip = (page - 1) * pageSize;

        var dbPage = await _persistenceService.GetMetricsPagedAsync(rangeSeconds, connectionId, skip, pageSize, blockedOnly: false, includeBlockingDetails: false);

        var extras = CollectExtras(connectionId, cutoff);
        return MergePageWithExtras(dbPage, extras, skip, pageSize, page);
    }

    /// <summary>
    /// Gets metrics for a custom date range.
    /// </summary>
    public async Task<PagedResult<MetricDataPoint>> GetMetricsByDateRangePageAsync(DateTime fromUtc, DateTime toUtc, int page, int pageSize, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return EmptyPage(page, pageSize);
        }

        var skip = (page - 1) * pageSize;

        var dbPage = await _persistenceService.GetMetricsByDateRangePagedAsync(fromUtc, toUtc, connectionId, skip, pageSize, blockedOnly: false, includeBlockingDetails: false);

        var extras = CollectExtras(connectionId, fromUtc);
        return MergePageWithExtras(dbPage, extras.Where(e => e.Timestamp <= toUtc).ToList(), skip, pageSize, page);
    }

    /// <summary>
    /// Gets query history summary with aggregation.
    /// </summary>
    public async Task<List<QueryPerformance>> GetQueryHistorySummaryAsync(double hours, string connectionId, string sortBy = "cpu")
    {
        var range = (int)(hours * 3600);
        
        if (string.IsNullOrEmpty(connectionId)) return new List<QueryPerformance>();
        
        var queries = await _persistenceService.GetQueryHistoryAsync(range, connectionId);
        
        var cutoffUtc = DateTime.UtcNow.AddSeconds(-range);
        queries = queries.Where(q => q.LastExecutionTime >= cutoffUtc).ToList();
        
        // Also get recent in-memory data
        var memRecent = _bufferService.GetRecentDataPoints(connectionId)
            .SelectMany(dp => dp.TopQueries)
            .Where(q => q.LastExecutionTime >= cutoffUtc)
            .ToList();
            
        queries.AddRange(memRecent);
        
        var queryGroups = queries
            .GroupBy(q => q.QueryHash)
            .Where(g => !string.IsNullOrEmpty(g.Key));
            
        var result = new List<QueryPerformance>();
        
        foreach (var g in queryGroups)
        {
            var max = g.OrderByDescending(q => q.ExecutionCount).First();
            
            result.Add(new QueryPerformance
            {
                QueryHash = max.QueryHash,
                QueryText = string.IsNullOrEmpty(max.QueryText) ? max.QueryTextPreview : max.QueryText,
                ExecutionCount = max.ExecutionCount,
                AvgCpuTimeMs = max.AvgCpuTimeMs,
                TotalCpuTimeMs = max.AvgCpuTimeMs * max.ExecutionCount,
                AvgLogicalReads = max.AvgLogicalReads,
                TotalLogicalReads = max.AvgLogicalReads * max.ExecutionCount,
                AvgLogicalWrites = max.AvgLogicalWrites,
                TotalLogicalWrites = max.AvgLogicalWrites * max.ExecutionCount,
                AvgElapsedTimeMs = max.AvgElapsedTimeMs,
                TotalElapsedTimeMs = max.AvgElapsedTimeMs * max.ExecutionCount,
                LastExecutionTime = max.LastExecutionTime,
                DatabaseName = max.DatabaseName ?? "Unknown",
                ExecutionPlan = null // Lazy load via separate API
            });
        }
        
        var sorted = sortBy switch
        {
            "io" => result.OrderByDescending(x => x.AvgLogicalReads),
            "elapsed" => result.OrderByDescending(x => x.AvgElapsedTimeMs),
            _ => result.OrderByDescending(x => x.TotalCpuTimeMs)
        };
        
        return sorted.Take(100).ToList();
    }

    /// <summary>
    /// Gets the latest metric data point.
    /// </summary>
    public MetricDataPoint? GetLatest(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;

        return _bufferService.GetLatest(connectionId);
    }

    /// <summary>
    /// Gets buffer health information.
    /// </summary>
    public BufferHealth GetBufferHealth()
    {
        return _bufferService.GetBufferHealth();
    }

    public async Task<PagedResult<MetricDataPoint>> GetBlockingHistoryPageAsync(int rangeSeconds, int page, int pageSize, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return EmptyPage(page, pageSize);
        }

        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        var skip = (page - 1) * pageSize;

        var dbPage = await _persistenceService.GetMetricsPagedAsync(rangeSeconds, connectionId, skip, pageSize, blockedOnly: true, includeBlockingDetails: true);

        var extras = CollectExtras(connectionId, cutoff)
            .Where(m => m.BlockedProcesses > 0 || m.BlockedQueries.Count > 0)
            .ToList();

        return MergePageWithExtras(dbPage, extras, skip, pageSize, page);
    }

    public async Task<string?> GetExecutionPlanAsync(string connectionId, string queryHash)
    {
         if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(queryHash)) return null;

         // Check recent buffer first (optional optimization, but good for real-time feel)
         var bufferPlan = _bufferService.GetRecentDataPoints(connectionId)
             .SelectMany(p => p.TopQueries)
             .Where(q => q.QueryHash == queryHash && !string.IsNullOrEmpty(q.ExecutionPlan))
             .OrderByDescending(q => q.LastExecutionTime)
             .Select(q => q.ExecutionPlan)
             .FirstOrDefault();

         if (!string.IsNullOrEmpty(bufferPlan))
         {
             return bufferPlan;
         }
         
         // Fallback to persistence
         return await _persistenceService.GetExecutionPlanAsync(connectionId, queryHash);
    }

    private List<MetricDataPoint> CollectExtras(string connectionId, DateTime cutoff)
    {
        var memRecent = _bufferService.GetRecentDataPoints(connectionId, cutoff).ToList();
        var pending = _bufferService.GetPendingDataPoints(connectionId, cutoff).ToList();

        return memRecent
            .Concat(pending)
            .Where(p => p.Timestamp >= cutoff)
            .OrderBy(p => p.Timestamp)
            .ToList();
    }

    private PagedResult<MetricDataPoint> MergePageWithExtras(PagedResult<MetricDataPoint> dbPage, List<MetricDataPoint> extras, int skip, int pageSize, int page)
    {
        var totalCount = dbPage.TotalCount + extras.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        // Extras are assumed to be sorted ascending already.
        var extrasStart = Math.Max(0, skip - dbPage.TotalCount);
        var resultItems = dbPage.Items.ToList();

        if (extrasStart < extras.Count && resultItems.Count < pageSize)
        {
            resultItems.AddRange(extras.Skip(extrasStart).Take(pageSize - resultItems.Count));
        }

        return new PagedResult<MetricDataPoint>
        {
            Items = resultItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    private static PagedResult<MetricDataPoint> EmptyPage(int page, int pageSize) => new()
    {
        Items = new List<MetricDataPoint>(),
        Page = page,
        PageSize = pageSize,
        TotalCount = 0,
        TotalPages = 0
    };
}
