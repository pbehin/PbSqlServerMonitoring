using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Manages in-memory buffering for metrics data.
/// Responsible for:
/// - Storing recent data points for fast dashboard access
/// - Queuing data for persistence
/// - Managing buffer capacity and cleanup
/// </summary>
public sealed class MetricsBufferService
{
    #region Constants
    
    /// <summary>Keep 15 minutes in memory for fast dashboard access</summary>
    private const int MemoryRetentionMinutes = 15;
    
    /// <summary>Sample interval in seconds</summary>
    private const int SampleIntervalSeconds = 3;

    /// <summary>Max in-memory recent datapoints</summary>
    private const int MaxRecentDataPoints = (MemoryRetentionMinutes * 60 / SampleIntervalSeconds) + 50;

    /// <summary>Cap pending save queue to avoid unbounded growth</summary>
    private const int MaxPendingSaveQueue = 5000;

    /// <summary>High-watermark ratio to trigger backpressure</summary>
    private const double PendingHighWatermarkRatio = 0.9;
    
    #endregion

    #region Fields
    
    private readonly ConcurrentQueue<MetricDataPoint> _recentDataPoints = new();
    private readonly ConcurrentQueue<MetricDataPoint> _pendingSaveQueue = new();
    private readonly ILogger<MetricsBufferService> _logger;
    
    private static readonly Meter Meter = new("PbSqlServerMonitoring.Metrics");
    private readonly Counter<long> _droppedPendingCounter = Meter.CreateCounter<long>(
        "metrics.pending_drops", 
        unit: "items", 
        description: "Dropped pending metrics due to full buffer");

    private readonly ObservableGauge<int> _pendingQueueGauge;
    private readonly ObservableGauge<int> _recentQueueGauge;
    
    private long _droppedPendingTotal;
    private DateTime? _lastDropUtc;
    
    #endregion

    #region Constructor
    
    public MetricsBufferService(ILogger<MetricsBufferService> logger)
    {
        _logger = logger;

        _pendingQueueGauge = Meter.CreateObservableGauge(
            "metrics.pending_queue_length",
            () => _pendingSaveQueue.Count,
            unit: "items",
            description: "Current length of pending metrics queue");
        _recentQueueGauge = Meter.CreateObservableGauge(
            "metrics.recent_queue_length",
            () => _recentDataPoints.Count,
            unit: "items",
            description: "Current length of recent metrics cache");
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Adds a data point to both recent cache and pending save queue.
    /// </summary>
    public void Enqueue(MetricDataPoint dataPoint)
    {
        EnqueueRecent(dataPoint);
        EnqueuePendingSave(dataPoint);
    }
    
    /// <summary>
    /// Gets recent data points for the specified server/database.
    /// </summary>
    public IEnumerable<MetricDataPoint> GetRecentDataPoints(string serverName, string databaseName, DateTime? cutoff = null)
    {
        var query = _recentDataPoints
            .Where(dp => dp.ServerName == serverName && dp.DatabaseName == databaseName);
            
        if (cutoff.HasValue)
        {
            query = query.Where(dp => dp.Timestamp >= cutoff.Value);
        }
        
        return query.OrderBy(dp => dp.Timestamp).ToList();
    }
    
    /// <summary>
    /// Gets the latest data point for the specified server/database.
    /// </summary>
    public MetricDataPoint? GetLatest(string serverName, string databaseName)
    {
        return _recentDataPoints
            .Where(p => p.ServerName == serverName && p.DatabaseName == databaseName)
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Dequeues all pending data points for persistence.
    /// </summary>
    public List<MetricDataPoint> DequeuePendingForSave()
    {
        var points = new List<MetricDataPoint>();
        while (_pendingSaveQueue.TryDequeue(out var point))
        {
            points.Add(point);
        }
        return points;
    }
    
    /// <summary>
    /// Re-queues points that failed to save.
    /// </summary>
    public void RequeueFailedPoints(IEnumerable<MetricDataPoint> points)
    {
        foreach (var point in points)
        {
            EnqueuePendingSave(point);
        }
    }
    
    /// <summary>
    /// Gets pending data points for a specific server/database without removing them.
    /// </summary>
    public IEnumerable<MetricDataPoint> GetPendingDataPoints(string serverName, string databaseName, DateTime cutoff)
    {
        return _pendingSaveQueue
            .Where(p => p.ServerName == serverName && p.DatabaseName == databaseName && p.Timestamp >= cutoff)
            .ToList();
    }
    
    /// <summary>
    /// Checks if save queue is empty.
    /// </summary>
    public bool IsPendingQueueEmpty => _pendingSaveQueue.IsEmpty;
    
    /// <summary>
    /// Gets buffer health information.
    /// </summary>
    public BufferHealth GetBufferHealth()
    {
        return new BufferHealth
        {
            PendingQueueLength = _pendingSaveQueue.Count,
            RecentQueueLength = _recentDataPoints.Count,
            DroppedPendingTotal = Interlocked.Read(ref _droppedPendingTotal),
            LastDropUtc = _lastDropUtc
        };
    }

    /// <summary>
    /// Indicates if backpressure should be applied to collection to avoid excessive drops.
    /// </summary>
    public bool ShouldThrottleCollection
        => _pendingSaveQueue.Count >= (int)(MaxPendingSaveQueue * PendingHighWatermarkRatio);
    
    /// <summary>
    /// Performs cleanup of old data points.
    /// </summary>
    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-MemoryRetentionMinutes);
        
        while (_recentDataPoints.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _recentDataPoints.TryDequeue(out _);
        }

        while (_recentDataPoints.Count > MaxRecentDataPoints)
        {
            _recentDataPoints.TryDequeue(out _);
        }
    }
    
    #endregion

    #region Private Methods
    
    private void EnqueueRecent(MetricDataPoint dataPoint)
    {
        _recentDataPoints.Enqueue(dataPoint);
    }

    private void EnqueuePendingSave(MetricDataPoint dataPoint)
    {
        var dropped = 0;
        while (_pendingSaveQueue.Count >= MaxPendingSaveQueue)
        {
            if (_pendingSaveQueue.TryDequeue(out _))
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            Interlocked.Add(ref _droppedPendingTotal, dropped);
            _lastDropUtc = DateTime.UtcNow;
            _droppedPendingCounter.Add(dropped);
            _logger.LogWarning("Dropped {Count} pending metrics due to full buffer (cap {Cap})", dropped, MaxPendingSaveQueue);
        }

        _pendingSaveQueue.Enqueue(dataPoint);
    }
    
    #endregion
}
