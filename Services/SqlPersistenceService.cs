using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

public sealed class SqlPersistenceService : IMetricsPersistenceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlPersistenceService> _logger;
    private readonly IConfiguration _configuration;
    private volatile bool _dbEnsured;
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

    public async Task EnsureDatabaseAsync()
    {
        if (_dbEnsured) return;

        if (!_applyMigrations)
        {
            _logger.LogInformation("Auto-migrations disabled (PB_MONITOR_AUTO_MIGRATE=false). Skipping EnsureDatabase.");
            _dbEnsured = true; // prevent repeat attempts
            await LogPendingMigrationsIfAny();
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            
            _logger.LogInformation("Applying database migrations...");
            await context.Database.MigrateAsync();
            _dbEnsured = true;
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply migrations.");
        }
    }

    public async Task SaveMetricsAsync(IEnumerable<MetricDataPoint> metrics, string serverName, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(databaseName);
        
        var metricsList = metrics.ToList();
        if (metricsList.Count == 0) return;
        if (!_dbEnsured) await EnsureDatabaseAsync();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            // Build snapshots using batch approach
            var snapshots = metricsList.Select(m => new MetricSnapshotEntity
            {
                Timestamp = m.Timestamp,
                ServerName = serverName,
                DatabaseName = databaseName,
                CpuPercent = m.CpuPercent,
                MemoryMb = m.MemoryMb,
                ActiveConnections = m.ActiveConnections,
                BlockedProcesses = m.BlockedProcesses,
                BufferCacheHitRatio = m.BufferCacheHitRatio,
                TopQueries = m.TopQueries?.Select(q => new QueryHistoryEntity
                {
                    QueryHash = q.QueryHash,
                    QueryText = Truncate(q.QueryTextPreview, MetricsConstants.MaxQueryTextLength),
                    DatabaseName = q.DatabaseName,
                    AvgCpuTimeMs = q.AvgCpuTimeMs,
                    ExecutionCount = q.ExecutionCount,
                    LastExecutionTime = q.LastExecutionTime,
                    AvgLogicalReads = q.AvgLogicalReads,
                    AvgLogicalWrites = q.AvgLogicalWrites,
                    AvgElapsedTimeMs = q.AvgElapsedTimeMs
                }).ToList() ?? [],
                BlockedQueries = m.BlockedQueries?.Select(b => new BlockingHistoryEntity
                {
                    SessionId = b.SessionId,
                    BlockingSessionId = b.BlockingSessionId,
                    QueryText = Truncate(b.QueryTextPreview, MetricsConstants.MaxQueryTextLength),
                    WaitTimeMs = b.WaitTimeMs,
                    WaitType = b.WaitType,
                    IsLeadBlocker = b.IsLeadBlocker
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

    public async Task<List<MetricDataPoint>> GetMetricsAsync(int rangeSeconds, string serverName, string databaseName)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        return await GetMetricsInternalAsync(s => s.Timestamp >= cutoff, serverName, databaseName);
    }

    public async Task<List<MetricDataPoint>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, string serverName, string databaseName)
    {
        return await GetMetricsInternalAsync(s => s.Timestamp >= from && s.Timestamp <= to, serverName, databaseName);
    }

    public async Task<PagedResult<MetricDataPoint>> GetMetricsPagedAsync(int rangeSeconds, string serverName, string databaseName, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-rangeSeconds);
        return await GetMetricsPagedInternalAsync(s => s.Timestamp >= cutoff, serverName, databaseName, skip, take, blockedOnly, includeBlockingDetails);
    }

    public async Task<PagedResult<MetricDataPoint>> GetMetricsByDateRangePagedAsync(DateTime from, DateTime to, string serverName, string databaseName, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false)
    {
        return await GetMetricsPagedInternalAsync(s => s.Timestamp >= from && s.Timestamp <= to, serverName, databaseName, skip, take, blockedOnly, includeBlockingDetails);
    }

    private async Task<List<MetricDataPoint>> GetMetricsInternalAsync(System.Linq.Expressions.Expression<Func<MetricSnapshotEntity, bool>> predicate, string serverName, string databaseName)
    {
         using var scope = _scopeFactory.CreateScope();
         var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

         try 
         {
             var entities = await context.MetricSnapshots
                .AsNoTracking()
                .Where(s => s.ServerName == (serverName ?? "") && s.DatabaseName == (databaseName ?? ""))
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

    private async Task<PagedResult<MetricDataPoint>> GetMetricsPagedInternalAsync(System.Linq.Expressions.Expression<Func<MetricSnapshotEntity, bool>> predicate, string serverName, string databaseName, int skip, int take, bool blockedOnly, bool includeBlockingDetails)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

        try
        {
            IQueryable<MetricSnapshotEntity> baseQuery = context.MetricSnapshots
                .AsNoTracking()
                .Where(s => s.ServerName == (serverName ?? "") && s.DatabaseName == (databaseName ?? ""))
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
                        QueryTextPreview = b.QueryText ?? "",
                        WaitTimeMs = b.WaitTimeMs,
                        WaitType = b.WaitType ?? "",
                        IsLeadBlocker = b.IsLeadBlocker
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

    public async Task<List<QuerySnapshot>> GetQueryHistoryAsync(int rangeSeconds, string serverName, string databaseName)
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
                .Where(q => q.Snapshot.ServerName == (serverName ?? "") 
                         && q.Snapshot.DatabaseName == (databaseName ?? "")
                         && q.Snapshot.Timestamp >= cutoff)
                .Select(q => new QuerySnapshot
                {
                     QueryHash = q.QueryHash ?? "",
                     QueryTextPreview = q.QueryText ?? "",
                     DatabaseName = q.DatabaseName,
                     AvgCpuTimeMs = q.AvgCpuTimeMs,
                     ExecutionCount = q.ExecutionCount,
                     // We use coalesce here to be safe, though migration enforces it
                     LastExecutionTime = q.LastExecutionTime ?? default,
                     AvgLogicalReads = q.AvgLogicalReads,
                     AvgLogicalWrites = q.AvgLogicalWrites,
                     AvgElapsedTimeMs = q.AvgElapsedTimeMs
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
}
