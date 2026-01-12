using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Background service for collecting SQL Server metrics.
/// Focused responsibility: Collect metrics at regular intervals.
/// Buffer management delegated to MetricsBufferService.
/// Query/retrieval delegated to MetricsQueryService.
/// 
/// Architecture: Uses IBackgroundTaskQueue for proper async task management
/// instead of fire-and-forget Task.Run patterns.
/// </summary>
public sealed class MetricsCollectionService : IHostedService, IDisposable
{
    #region Fields
    
    private readonly MultiConnectionService _multiConnectionService;
    private readonly ConnectionService _connectionService;
    private readonly ServerHealthService _healthService;
    private readonly BlockingService _blockingService;
    private readonly QueryPerformanceService _queryService;
    private readonly MetricsBufferService _bufferService;
    private readonly IMetricsPersistenceService _persistenceService;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;
    private readonly ILogger<MetricsCollectionService> _logger;
    
    private readonly SemaphoreSlim _collectLock = new(1, 1);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _runnerTask;
    private int _ticksSinceSave;
    private DateTime _lastThrottleLogUtc = DateTime.MinValue;
    
    #endregion

    #region Constructor
    
    public MetricsCollectionService(
        MultiConnectionService multiConnectionService,
        ConnectionService connectionService,
        ServerHealthService healthService,
        BlockingService blockingService,
        QueryPerformanceService queryService,
        MetricsBufferService bufferService,
        IMetricsPersistenceService persistenceService,
        IBackgroundTaskQueue backgroundTaskQueue,
        ILogger<MetricsCollectionService> logger)
    {
        _multiConnectionService = multiConnectionService ?? throw new ArgumentNullException(nameof(multiConnectionService));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _blockingService = blockingService ?? throw new ArgumentNullException(nameof(blockingService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _bufferService = bufferService ?? throw new ArgumentNullException(nameof(bufferService));
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _backgroundTaskQueue = backgroundTaskQueue ?? throw new ArgumentNullException(nameof(backgroundTaskQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    #endregion

    #region IHostedService Implementation
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting metrics collection service (interval: {Interval}s)", 
            MetricsConstants.SampleIntervalSeconds);
        
        await _persistenceService.EnsureDatabaseAsync();
        
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(MetricsConstants.SampleIntervalSeconds));
        _runnerTask = Task.Run(() => RunAsync(timer, _cts.Token), cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping metrics collection service");
        _cts.Cancel();
        
        if (_runnerTask != null)
        {
            try
            {
                await _runnerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }

        await SavePendingMetricsAsync();
    }
    
    #endregion

    #region Collection Loop
    
    private async Task RunAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CollectMetricsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task CollectMetricsAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        if (_bufferService.ShouldThrottleCollection)
        {
            if ((DateTime.UtcNow - _lastThrottleLogUtc).TotalSeconds > 30)
            {
                _lastThrottleLogUtc = DateTime.UtcNow;
                _logger.LogWarning("Skipping collection tick due to high pending queue. Pending={Pending}", 
                    _bufferService.GetBufferHealth().PendingQueueLength);
            }
            return;
        }

        if (!await _collectLock.WaitAsync(0, cancellationToken))
        {
            return; // Skip overlapping runs
        }

        try
        {
            var enabledConnections = _multiConnectionService.GetEnabledConnections();
            var collectionTasks = new List<Task>();
            
            foreach (var conn in enabledConnections)
            {
                // Capture loop variable (although C# 5+ handles this, it's safer for clarity)
                var connection = conn;
                
                collectionTasks.Add(Task.Run(async () => 
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    
                    try 
                    {
                        var connectionString = _multiConnectionService.GetConnectionString(connection.Id);
                        if (string.IsNullOrEmpty(connectionString)) return;

                        // Pass context to services
                        var health = await _healthService.GetServerHealthAsync(connectionString);
                        
                        if (health.IsConnected)
                        {
                            var blockingTask = _blockingService.GetBlockingSessionsAsync(connectionString);
                            var topQueriesTask = _queryService.GetTopCpuQueriesAsync(MetricsConstants.DefaultTopN, connectionString, includeExecutionPlan: true);

                            await Task.WhenAll(blockingTask, topQueriesTask);
                            
                            var blockingSessions = await blockingTask;
                            var topQueries = await topQueriesTask;
                            
                            var dataPoint = CreateDataPoint(connection, health, topQueries, blockingSessions);
                            
                            _bufferService.Enqueue(dataPoint);
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogWarning(innerEx, "Failed to collect metrics for connection {ConnectionName} ({Server})", 
                            connection.Name, connection.Server);
                    }
                }, cancellationToken));
            }
            
            if (collectionTasks.Any())
            {
                await Task.WhenAll(collectionTasks);
                // Record success if we got here without crashing
                _bufferService.RecordCollectionResult(true);
            }
            
            _bufferService.Cleanup();
            
            if (_ticksSinceSave++ >= MetricsConstants.TicksBetweenSaves)
            {
                _ticksSinceSave = 0;
                
                // Use background task queue instead of fire-and-forget
                _backgroundTaskQueue.QueueBackgroundWorkItem(async ct =>
                {
                    await SavePendingMetricsAsync();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect metrics");
            _bufferService.RecordCollectionResult(false, ex.Message);
        }
        finally
        {
            _collectLock.Release();
        }
    }
    
    private static MetricDataPoint CreateDataPoint(
        ServerConnection connection,
        ServerHealth health,
        List<QueryPerformance> topQueries,
        List<BlockingSession> blockingSessions)
    {
        return new MetricDataPoint
        {
            Timestamp = DateTime.UtcNow,
            ConnectionId = connection.Id,
            ServerName = connection.Server,
            DatabaseName = connection.Database,
            CpuPercent = health.CpuUsagePercent,
            MemoryMb = health.MemoryUsedMb,
            ActiveConnections = health.ActiveConnections,
            BlockedProcesses = blockingSessions.Count,
            BufferCacheHitRatio = health.BufferCacheHitRatio,
            TopQueries = topQueries.Select(q => new QuerySnapshot 
            {
                QueryHash = q.QueryHash,
                QueryText = q.QueryText,
                QueryTextPreview = q.QueryText.Length > MetricsConstants.MaxQueryTextPreviewLength 
                    ? q.QueryText[..(MetricsConstants.MaxQueryTextPreviewLength - 3)] + "..." 
                    : q.QueryText,
                AvgCpuTimeMs = q.AvgCpuTimeMs,
                ExecutionCount = q.ExecutionCount,
                AvgLogicalReads = q.AvgLogicalReads,
                AvgLogicalWrites = q.AvgLogicalWrites,
                AvgElapsedTimeMs = q.AvgElapsedTimeMs,
                DatabaseName = q.DatabaseName,
                LastExecutionTime = q.LastExecutionTime,
                ExecutionPlan = TruncateExecutionPlan(q.ExecutionPlan)
            }).ToList(),
            BlockedQueries = blockingSessions.Select(b => new BlockingSnapshot
            {
                SessionId = b.SessionId,
                BlockingSessionId = b.BlockingSessionId,
                QueryText = b.QueryText,
                QueryTextPreview = b.QueryText.Length > MetricsConstants.MaxQueryTextPreviewLength 
                    ? b.QueryText[..(MetricsConstants.MaxQueryTextPreviewLength - 3)] + "..." 
                    : b.QueryText,
                WaitTimeMs = b.WaitTimeMs,
                WaitType = b.WaitType,
                IsLeadBlocker = b.IsLeadBlocker,
                ExecutionPlan = TruncateExecutionPlan(b.ExecutionPlan)
            }).ToList()
        };
    }
    
    /// <summary>Truncate execution plan to limit database storage size</summary>
    private static string? TruncateExecutionPlan(string? plan)
    {
        if (string.IsNullOrEmpty(plan)) return null;
        return plan.Length <= MetricsConstants.MaxExecutionPlanLength 
            ? plan 
            : plan[..MetricsConstants.MaxExecutionPlanLength];
    }
    
    #endregion

    #region Persistence
    
    private async Task SavePendingMetricsAsync()
    {
        if (_bufferService.IsPendingQueueEmpty) return;
        
        // Prevent concurrent save operations
        if (!await _saveLock.WaitAsync(0))
        {
            return;
        }
        
        try
        {
            var pointsToSave = _bufferService.DequeuePendingForSave();
            
            if (pointsToSave.Count > 0)
            {
                try
                {
                    var groups = pointsToSave.GroupBy(p => p.ConnectionId);
                    foreach (var g in groups)
                    {
                        await _persistenceService.SaveMetricsAsync(g, g.Key);
                    }
                    
                    _logger.LogDebug("Saved {Count} metrics to SQL", pointsToSave.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save pending metrics.");
                    _bufferService.RequeueFailedPoints(pointsToSave);
                }
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
    
    #endregion

    #region IDisposable
    
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try 
        {
            _cts.Cancel();
        } 
        catch (ObjectDisposedException) { } // Ignore if already disposed
        
        _cts.Dispose();
        _collectLock.Dispose();
        _saveLock.Dispose();
    }
    
    #endregion
}
