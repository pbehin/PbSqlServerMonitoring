namespace PbSqlServerMonitoring.Models;

// ============================================================
// Query Performance Models
// ============================================================

/// <summary>
/// Represents query performance metrics from SQL Server DMVs.
/// </summary>
public sealed class QueryPerformance
{
    /// <summary>Query hash for identifying unique queries</summary>
    public string QueryHash { get; set; } = string.Empty;
    
    /// <summary>The SQL query text</summary>
    public string QueryText { get; set; } = string.Empty;
    
    /// <summary>Database where the query executed</summary>
    public string DatabaseName { get; set; } = string.Empty;
    
    /// <summary>Number of times this query has executed</summary>
    public long ExecutionCount { get; set; }
    
    /// <summary>Total CPU time in milliseconds</summary>
    public double TotalCpuTimeMs { get; set; }
    
    /// <summary>Average CPU time per execution (ms)</summary>
    public double AvgCpuTimeMs { get; set; }
    
    /// <summary>Total elapsed time in milliseconds</summary>
    public double TotalElapsedTimeMs { get; set; }
    
    /// <summary>Average elapsed time per execution (ms)</summary>
    public double AvgElapsedTimeMs { get; set; }
    
    /// <summary>Total logical reads (pages read from cache)</summary>
    public long TotalLogicalReads { get; set; }
    
    /// <summary>Average logical reads per execution</summary>
    public long AvgLogicalReads { get; set; }
    
    /// <summary>Total logical writes</summary>
    public long TotalLogicalWrites { get; set; }
    
    /// <summary>Average logical writes per execution</summary>
    public long AvgLogicalWrites { get; set; }
    
    /// <summary>Total physical reads (pages read from disk)</summary>
    public long TotalPhysicalReads { get; set; }
    
    /// <summary>Average physical reads per execution</summary>
    public long AvgPhysicalReads { get; set; }
    
    /// <summary>When the query last executed</summary>
    public DateTime LastExecutionTime { get; set; }
    
    /// <summary>When the query plan was created</summary>
    public DateTime CreationTime { get; set; }
    
    /// <summary>XML execution plan for this query</summary>
    public string? ExecutionPlan { get; set; }
}

// ============================================================
// Missing Index Models
// ============================================================

/// <summary>
/// Represents a missing index recommendation.
/// </summary>
public sealed class MissingIndex
{
    public string DatabaseName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;
    
    /// <summary>Columns used in equality predicates (=)</summary>
    public string EqualityColumns { get; set; } = string.Empty;
    
    /// <summary>Columns used in inequality predicates (&lt;, &gt;, BETWEEN)</summary>
    public string InequalityColumns { get; set; } = string.Empty;
    
    /// <summary>Columns to include in leaf level</summary>
    public string IncludedColumns { get; set; } = string.Empty;
    
    /// <summary>Calculated improvement score (higher = more beneficial)</summary>
    public double ImprovementMeasure { get; set; }
    
    /// <summary>Number of seeks that would have used this index</summary>
    public long UserSeeks { get; set; }
    
    /// <summary>Number of scans that would have used this index</summary>
    public long UserScans { get; set; }
    
    /// <summary>Average query cost</summary>
    public double AvgTotalUserCost { get; set; }
    
    /// <summary>Expected performance improvement percentage</summary>
    public double AvgUserImpact { get; set; }
    
    /// <summary>Generated CREATE INDEX statement</summary>
    public string CreateIndexStatement { get; set; } = string.Empty;
}

// ============================================================
// Blocking and Lock Models
// ============================================================

/// <summary>
/// Represents a blocking session.
/// </summary>
public sealed class BlockingSession
{
    public int SessionId { get; set; }
    
    /// <summary>Session ID of the blocker (null if this is the lead blocker)</summary>
    public int? BlockingSessionId { get; set; }
    
    public string Status { get; set; } = string.Empty;
    public string WaitType { get; set; } = string.Empty;
    
    /// <summary>Time waiting in milliseconds</summary>
    public int WaitTimeMs { get; set; }
    
    public string WaitResource { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public int CpuTime { get; set; }
    public long LogicalReads { get; set; }
    public int MemoryUsageKb { get; set; }
    public DateTime? StartTime { get; set; }
    
    /// <summary>True if this session is at the head of a blocking chain</summary>
    public bool IsLeadBlocker { get; set; }
    
    /// <summary>Number of sessions this one is blocking</summary>
    public int BlockedCount { get; set; }
    
    /// <summary>XML execution plan for this query</summary>
    public string? ExecutionPlan { get; set; }
}

/// <summary>
/// Represents lock information.
/// </summary>
public sealed class LockInfo
{
    public int SessionId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>Type of resource locked (DATABASE, TABLE, PAGE, ROW, etc.)</summary>
    public string ResourceType { get; set; } = string.Empty;
    
    /// <summary>Lock mode (S, X, IS, IX, etc.)</summary>
    public string RequestMode { get; set; } = string.Empty;
    
    /// <summary>Lock status (GRANT, WAIT, CONVERT)</summary>
    public string RequestStatus { get; set; } = string.Empty;
    
    /// <summary>Number of locks of this type</summary>
    public int RequestCount { get; set; }
    
    public string QueryText { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
}

// ============================================================
// Server Health Models
// ============================================================

/// <summary>
/// Represents SQL Server health status.
/// </summary>
public sealed class ServerHealth
{
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string ServerName { get; set; } = string.Empty;
    public string SqlServerVersion { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    
    public DateTime ServerStartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    
    /// <summary>Number of active user connections</summary>
    public int ActiveConnections { get; set; }
    
    /// <summary>Number of currently blocked processes</summary>
    public int BlockedProcesses { get; set; }
    
    /// <summary>Approximate CPU usage percentage</summary>
    public double CpuUsagePercent { get; set; }
    
    /// <summary>Memory used by SQL Server (MB)</summary>
    public long MemoryUsedMb { get; set; }
    
    /// <summary>Buffer cache hit ratio (percentage)</summary>
    public long BufferCacheHitRatio { get; set; }
}

// ============================================================
// Running Queries Models
// ============================================================

/// <summary>
/// Represents a currently executing query.
/// </summary>
public sealed class RunningQuery
{
    public int SessionId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    
    /// <summary>Query status (running, suspended, etc.)</summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>Type of command (SELECT, INSERT, etc.)</summary>
    public string Command { get; set; } = string.Empty;
    
    public string WaitType { get; set; } = string.Empty;
    public int WaitTimeMs { get; set; }
    public int CpuTimeMs { get; set; }
    
    /// <summary>Total time since query started (ms)</summary>
    public int ElapsedTimeMs { get; set; }
    
    public long LogicalReads { get; set; }
    public long Writes { get; set; }
    public DateTime StartTime { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    
    /// <summary>Completion percentage for long operations</summary>
    public double PercentComplete { get; set; }
    
    /// <summary>Session ID blocking this query (if any)</summary>
    public int? BlockingSessionId { get; set; }
}
