using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

public sealed class SqlPersistenceService : IMetricsPersistenceService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlPersistenceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _dbInitLock = new(1, 1);
    private int _dbEnsuredFlag; // 0 = not ensured, 1 = ensured (Interlocked-safe)
    private readonly bool _applyMigrations;
    private DateTime _nextCleanupUtc = DateTime.MinValue;
    private readonly int _retentionDays;
    private readonly TimeSpan _cleanupInterval;
    private readonly int _cleanupBatchSize;

    public SqlPersistenceService(IServiceScopeFactory scopeFactory, ILogger<SqlPersistenceService> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        var autoMigrateSetting = Environment.GetEnvironmentVariable("PB_MONITOR_AUTO_MIGRATE");
        _applyMigrations = !string.Equals(autoMigrateSetting, "false", StringComparison.OrdinalIgnoreCase);

        _retentionDays = ResolveIntSetting("Persistence:RetentionDays", "PB_MONITOR_RETENTION_DAYS", 
            MetricsConstants.DefaultRetentionDays, MetricsConstants.MinRetentionDays, MetricsConstants.MaxRetentionDays);
        var cleanupMinutes = ResolveIntSetting("Persistence:CleanupIntervalMinutes", "PB_MONITOR_CLEANUP_INTERVAL_MINUTES", 
            MetricsConstants.DefaultCleanupIntervalMinutes, 5, 1440);
        _cleanupInterval = TimeSpan.FromMinutes(cleanupMinutes);
        _cleanupBatchSize = _configuration.GetValue("Persistence:CleanupBatchSize", MetricsConstants.CleanupBatchSize);
    }

    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: already ensured
        if (Interlocked.CompareExchange(ref _dbEnsuredFlag, 0, 0) == 1) return;

        // Use lock to ensure only one thread runs migrations
        await _dbInitLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (Interlocked.CompareExchange(ref _dbEnsuredFlag, 0, 0) == 1) return;

            if (!_applyMigrations)
            {
                _logger.LogInformation("Auto-migrations disabled (PB_MONITOR_AUTO_MIGRATE=false). Skipping EnsureDatabase.");
                Interlocked.Exchange(ref _dbEnsuredFlag, 1);
                await LogPendingMigrationsIfAny();
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            
            _logger.LogInformation("Applying database migrations...");
            await context.Database.MigrateAsync(cancellationToken);
            Interlocked.Exchange(ref _dbEnsuredFlag, 1);
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Database migration was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply migrations.");
        }
        finally
        {
            _dbInitLock.Release();
        }
    }

    public async Task SaveMetricsAsync(IEnumerable<MetricDataPoint> metrics, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(connectionId);
        
        var metricsList = metrics.ToList();
        if (metricsList.Count == 0) return;
        if (Interlocked.CompareExchange(ref _dbEnsuredFlag, 0, 0) == 0) await EnsureDatabaseAsync();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            // Build snapshots using batch approach
            var snapshots = metricsList.Select(m => new MetricSnapshotEntity
            {
                Timestamp = m.Timestamp,
                ConnectionId = connectionId,
                ServerName = m.ServerName,
                DatabaseName = m.DatabaseName,
                CpuPercent = m.CpuPercent,
                MemoryMb = m.MemoryMb,
                ActiveConnections = m.ActiveConnections,
                BlockedProcesses = m.BlockedProcesses,
                BufferCacheHitRatio = m.BufferCacheHitRatio,
                TopQueries = m.TopQueries?.Select(q => new QueryHistoryEntity
                {
                    QueryHash = q.QueryHash,
                    QueryText = Truncate(q.QueryText, MetricsConstants.MaxQueryTextLength),
                    DatabaseName = q.DatabaseName,
                    AvgCpuTimeMs = q.AvgCpuTimeMs,
                    ExecutionCount = q.ExecutionCount,
                    LastExecutionTime = q.LastExecutionTime,
                    AvgLogicalReads = q.AvgLogicalReads,
                    AvgLogicalWrites = q.AvgLogicalWrites,
                    AvgElapsedTimeMs = q.AvgElapsedTimeMs,
                    ExecutionPlan = q.ExecutionPlan
                }).ToList() ?? [],
                BlockedQueries = m.BlockedQueries?.Select(b => new BlockingHistoryEntity
                {
                    SessionId = b.SessionId,
                    BlockingSessionId = b.BlockingSessionId,
                    QueryText = Truncate(b.QueryText, MetricsConstants.MaxQueryTextLength),
                    WaitTimeMs = b.WaitTimeMs,
                    WaitType = b.WaitType,
                    IsLeadBlocker = b.IsLeadBlocker,
                    ExecutionPlan = b.ExecutionPlan
                }).ToList() ?? []
            }).ToList();

            // Use AddRange for batch insert
            context.MetricSnapshots.AddRange(snapshots);
            await context.SaveChangesAsync();
            
            await CleanupIfDueAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metrics via EF.");
            throw; // Re-throw to allow caller to handle (e.g., requeue)
        }
    }

    public async Task<List<MetricDataPoint>> GetMetricsAsync(int rangeSeconds, string connectionId)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        return await GetMetricsInternalAsync(s => s.Timestamp >= cutoff, connectionId);
    }

    public async Task<List<MetricDataPoint>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, string connectionId)
    {
        return await GetMetricsInternalAsync(s => s.Timestamp >= from && s.Timestamp <= to, connectionId);
    }

    public async Task<PagedResult<MetricDataPoint>> GetMetricsPagedAsync(int rangeSeconds, string connectionId, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        return await GetMetricsPagedInternalAsync(s => s.Timestamp >= cutoff, connectionId, skip, take, blockedOnly, includeBlockingDetails);
    }

    public async Task<PagedResult<MetricDataPoint>> GetMetricsByDateRangePagedAsync(DateTime from, DateTime to, string connectionId, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false)
    {
        return await GetMetricsPagedInternalAsync(s => s.Timestamp >= from && s.Timestamp <= to, connectionId, skip, take, blockedOnly, includeBlockingDetails);
    }

    private async Task<List<MetricDataPoint>> GetMetricsInternalAsync(System.Linq.Expressions.Expression<Func<MetricSnapshotEntity, bool>> predicate, string connectionId)
    {
         using var scope = _scopeFactory.CreateScope();
         var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

         try 
         {
             var entities = await context.MetricSnapshots
                .AsNoTracking()
                .Where(s => s.ConnectionId == (connectionId ?? ""))
                .Where(predicate)
                .Include(s => s.TopQueries)
                .Include(s => s.BlockedQueries)
                .OrderBy(s => s.Timestamp)
                .AsSplitQuery() 
                .ToListAsync();

             return entities.Select(MapToDataPoint).ToList();
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Failed to load metrics via EF.");
             return [];
         }
    }

    private async Task<PagedResult<MetricDataPoint>> GetMetricsPagedInternalAsync(System.Linq.Expressions.Expression<Func<MetricSnapshotEntity, bool>> predicate, string connectionId, int skip, int take, bool blockedOnly, bool includeBlockingDetails)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

        try
        {
            IQueryable<MetricSnapshotEntity> baseQuery = context.MetricSnapshots
                .AsNoTracking()
                .Where(s => s.ConnectionId == (connectionId ?? ""))
                .Where(predicate);

            if (blockedOnly)
            {
                baseQuery = baseQuery.Where(s => s.BlockedProcesses > 0);
            }

            baseQuery = baseQuery.OrderBy(s => s.Timestamp);

            var totalCount = await baseQuery.CountAsync();

            IQueryable<MetricSnapshotEntity> pageQuery = baseQuery.Skip(skip).Take(take);

            if (includeBlockingDetails)
            {
                pageQuery = pageQuery.Include(s => s.BlockedQueries);
            }

            var entities = await pageQuery.Select(s => new MetricDataPoint
            {
                Timestamp = s.Timestamp,
                ServerName = s.ServerName,
                DatabaseName = s.DatabaseName,
                CpuPercent = s.CpuPercent,
                MemoryMb = (long)s.MemoryMb,
                ActiveConnections = s.ActiveConnections,
                BlockedProcesses = s.BlockedProcesses,
                BufferCacheHitRatio = (long)s.BufferCacheHitRatio,
                BlockedQueries = includeBlockingDetails
                    ? s.BlockedQueries.Select(b => new BlockingSnapshot
                    {
                        SessionId = b.SessionId,
                        BlockingSessionId = b.BlockingSessionId,
                        QueryText = b.QueryText ?? "",
                        QueryTextPreview = b.QueryText ?? "",
                        WaitTimeMs = b.WaitTimeMs,
                        WaitType = b.WaitType ?? "",
                        IsLeadBlocker = b.IsLeadBlocker,
                        ExecutionPlan = b.ExecutionPlan
                    }).ToList()
                    : new List<BlockingSnapshot>()
            }).ToListAsync();

            return new PagedResult<MetricDataPoint>
            {
                Items = entities,
                Page = (skip / take) + 1,
                PageSize = take,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / take)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load paged metrics via EF.");
            return new PagedResult<MetricDataPoint>
            {
                Items = [],
                Page = (skip / take) + 1,
                PageSize = take,
                TotalCount = 0,
                TotalPages = 0
            };
        }
    }

    public async Task<string?> GetExecutionPlanAsync(string connectionId, string queryHash)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

        try
        {
            // Find the most recent execution plan for this query hash
            var plan = await context.QueryHistory
                .AsNoTracking()
                .Where(q => q.QueryHash == queryHash && q.Snapshot.ConnectionId == connectionId)
                .OrderByDescending(q => q.LastExecutionTime)
                .Select(q => q.ExecutionPlan)
                .FirstOrDefaultAsync();

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load execution plan via EF.");
            return null;
        }
    }

    public async Task<List<QuerySnapshot>> GetQueryHistoryAsync(int rangeSeconds, string connectionId)
    {
         using var scope = _scopeFactory.CreateScope();
         var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

         try 
         {
             var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
             
             // Optimized query: Select only needed columns from QueryHistory JOIN MetricSnapshots
             // Avoids fetching BlockingHistory or full MetricSnapshot objects
             var queries = await context.QueryHistory
                .AsNoTracking()
                .Where(q => q.Snapshot.ConnectionId == (connectionId ?? "") 
                         && q.Snapshot.Timestamp >= cutoff)
                .Select(q => new QuerySnapshot
                {
                     QueryHash = q.QueryHash ?? "",
                     QueryText = q.QueryText ?? "",
                     QueryTextPreview = q.QueryText ?? "",
                     DatabaseName = q.DatabaseName,
                     AvgCpuTimeMs = q.AvgCpuTimeMs,
                     ExecutionCount = q.ExecutionCount,
                     // We use coalesce here to be safe, though migration enforces it
                     LastExecutionTime = q.LastExecutionTime ?? default,
                     AvgLogicalReads = q.AvgLogicalReads,
                     AvgLogicalWrites = q.AvgLogicalWrites,
                     AvgElapsedTimeMs = q.AvgElapsedTimeMs,
                     ExecutionPlan = null // Optimization: Don't load plan for history summary (lazy load via API)
                })
                .ToListAsync();

             return queries;
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Failed to load query history via EF.");
             return [];
         }
    }
    
    private static MetricDataPoint MapToDataPoint(MetricSnapshotEntity e)
    {
        return new MetricDataPoint
        {
            Timestamp = e.Timestamp,
            ConnectionId = e.ConnectionId ?? "",
            ServerName = e.ServerName,
            DatabaseName = e.DatabaseName,
            CpuPercent = e.CpuPercent,
            MemoryMb = (long)e.MemoryMb,
            ActiveConnections = e.ActiveConnections,
            BlockedProcesses = e.BlockedProcesses,
            BufferCacheHitRatio = (long)e.BufferCacheHitRatio,
            TopQueries = e.TopQueries.Select(q => new QuerySnapshot
            {
                QueryHash = q.QueryHash ?? "",
                QueryText = q.QueryText ?? "",
                QueryTextPreview = q.QueryText ?? "",
                DatabaseName = q.DatabaseName,
                AvgCpuTimeMs = q.AvgCpuTimeMs,
                ExecutionCount = q.ExecutionCount,
                LastExecutionTime = q.LastExecutionTime ?? default,
                AvgLogicalReads = q.AvgLogicalReads,
                AvgLogicalWrites = q.AvgLogicalWrites,
                AvgElapsedTimeMs = q.AvgElapsedTimeMs
            }).ToList(),
            BlockedQueries = e.BlockedQueries.Select(b => new BlockingSnapshot
            {
                SessionId = b.SessionId,
                BlockingSessionId = b.BlockingSessionId,
                QueryText = b.QueryText ?? "",
                QueryTextPreview = b.QueryText ?? "",
                WaitTimeMs = b.WaitTimeMs,
                WaitType = b.WaitType ?? "",
                IsLeadBlocker = b.IsLeadBlocker
            }).ToList()
        };
    }
    
    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Cleanup in batches to avoid long-running transactions on large datasets.
    /// </summary>
    private async Task CleanupIfDueAsync(MonitorDbContext context)
    {
        var now = DateTime.UtcNow;
        if (now < _nextCleanupUtc) return;

        _nextCleanupUtc = now + _cleanupInterval;

        try
        {
            var cutoff = now.AddDays(-_retentionDays);
            int totalDeleted = 0;
            int deleted;

            // Batch delete to avoid long-running transactions
            do
            {
                deleted = await context.MetricSnapshots
                    .Where(s => s.Timestamp < cutoff)
                    .Take(_cleanupBatchSize)
                    .ExecuteDeleteAsync();
                totalDeleted += deleted;
            } while (deleted >= _cleanupBatchSize);

            if (totalDeleted > 0)
            {
                _logger.LogInformation("Deleted {Count} expired metric snapshots (older than {Retention} days)", totalDeleted, _retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old metric snapshots");
        }
    }

    private int ResolveIntSetting(string configKey, string envName, int defaultValue, int min, int max)
    {
        var rawEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(rawEnv) && int.TryParse(rawEnv, out var envVal))
        {
            return Math.Clamp(envVal, min, max);
        }

        var rawConfig = _configuration[configKey];
        if (!string.IsNullOrWhiteSpace(rawConfig) && int.TryParse(rawConfig, out var cfgVal))
        {
            return Math.Clamp(cfgVal, min, max);
        }

        return defaultValue;
    }

    private async Task LogPendingMigrationsIfAny()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            var pending = await context.Database.GetPendingMigrationsAsync();
            var pendingList = pending.ToList();
            if (pendingList.Count > 0)
            {
                _logger.LogWarning("Auto-migrations disabled and {Count} migrations are pending: {Names}", pendingList.Count, string.Join(", ", pendingList));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not inspect pending migrations while auto-migrations are disabled");
        }
    }
    
    /// <summary>
    /// Disposes the database initialization lock.
    /// </summary>
    public void Dispose()
    {
        _dbInitLock.Dispose();
    }
}
