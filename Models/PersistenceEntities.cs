using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PbSqlServerMonitoring.Models;

[Table("MetricSnapshots")]
public class MetricSnapshotEntity
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    public DateTime Timestamp { get; set; }
    
    [Required]
    [MaxLength(128)]
    public string ServerName { get; set; } = "";
    
    [Required]
    [MaxLength(128)]
    public string DatabaseName { get; set; } = "";
    
    public double CpuPercent { get; set; }
    public double MemoryMb { get; set; }
    public int ActiveConnections { get; set; }
    public int BlockedProcesses { get; set; }
    public double BufferCacheHitRatio { get; set; }
    
    public List<QueryHistoryEntity> TopQueries { get; set; } = new();
    public List<BlockingHistoryEntity> BlockedQueries { get; set; } = new();
}

[Table("QueryHistory")]
public class QueryHistoryEntity
{
    [Key]
    public long Id { get; set; }
    
    public long SnapshotId { get; set; }
    [ForeignKey(nameof(SnapshotId))]
    public MetricSnapshotEntity Snapshot { get; set; } = null!;
    
    [MaxLength(64)]
    public string? QueryHash { get; set; }
    
    [MaxLength(500)]
    public string? QueryText { get; set; }
    
    [MaxLength(128)]
    public string? DatabaseName { get; set; }
    
    public double AvgCpuTimeMs { get; set; }
    public long ExecutionCount { get; set; }
    
    public DateTime? LastExecutionTime { get; set; }

    public long AvgLogicalReads { get; set; }
    public long AvgLogicalWrites { get; set; }
    public double AvgElapsedTimeMs { get; set; }
}

[Table("BlockingHistory")]
public class BlockingHistoryEntity
{
    [Key]
    public long Id { get; set; }
    
    public long SnapshotId { get; set; }
    [ForeignKey(nameof(SnapshotId))]
    public MetricSnapshotEntity Snapshot { get; set; } = null!;
    
    public int SessionId { get; set; }
    public int? BlockingSessionId { get; set; }
    
    [MaxLength(500)]
    public string? QueryText { get; set; }
    
    public long WaitTimeMs { get; set; }
    
    [MaxLength(64)]
    public string? WaitType { get; set; }
    
    public bool IsLeadBlocker { get; set; }
}
