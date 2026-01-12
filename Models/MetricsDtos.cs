using System.ComponentModel.DataAnnotations;

namespace PbSqlServerMonitoring.Models;

/// <summary>
/// Data point representing metrics collected at a specific timestamp.
/// </summary>
public sealed class MetricDataPoint
{
    public DateTime Timestamp { get; set; }
    [Required]
    [MaxLength(64)]
    public string ConnectionId { get; set; } = "";

    [Required]
    [MaxLength(128)]
    public string ServerName { get; set; } = "";
    
    [Required]
    [MaxLength(128)]
    public string DatabaseName { get; set; } = "";
    public double CpuPercent { get; set; }
    public long MemoryMb { get; set; }
    public int ActiveConnections { get; set; }
    public int BlockedProcesses { get; set; }
    public long BufferCacheHitRatio { get; set; }
    public List<QuerySnapshot> TopQueries { get; set; } = new();
    public List<BlockingSnapshot> BlockedQueries { get; set; } = new();
}

/// <summary>
/// Snapshot of query performance at a point in time.
/// </summary>
public sealed class QuerySnapshot
{
    public string QueryHash { get; set; } = "";
    /// <summary>Full query text for detailed views</summary>
    public string QueryText { get; set; } = "";
    /// <summary>Truncated preview for table display</summary>
    public string QueryTextPreview { get; set; } = "";
    public double AvgCpuTimeMs { get; set; }
    public long ExecutionCount { get; set; }
    public long AvgLogicalReads { get; set; }
    public long AvgLogicalWrites { get; set; }
    public double AvgElapsedTimeMs { get; set; }
    public string? DatabaseName { get; set; }
    public DateTime LastExecutionTime { get; set; }
    /// <summary>XML execution plan (optional, can be large)</summary>
    public string? ExecutionPlan { get; set; }
}

/// <summary>
/// Snapshot of blocking activity at a point in time.
/// </summary>
public sealed class BlockingSnapshot
{
    public int SessionId { get; set; }
    public int? BlockingSessionId { get; set; }
    /// <summary>Full query text for detailed views</summary>
    public string QueryText { get; set; } = "";
    /// <summary>Truncated preview for table display</summary>
    public string QueryTextPreview { get; set; } = "";
    public long WaitTimeMs { get; set; }
    public string WaitType { get; set; } = "";
    public bool IsLeadBlocker { get; set; }
    /// <summary>XML execution plan (optional, can be large)</summary>
    public string? ExecutionPlan { get; set; }
}

/// <summary>
/// Health information about internal data buffers.
/// </summary>
public sealed class BufferHealth
{
    public int PendingQueueLength { get; set; }
    public int RecentQueueLength { get; set; }
    public long DroppedPendingTotal { get; set; }
    public DateTime? LastDropUtc { get; set; }
}
