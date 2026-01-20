namespace PbSqlServerMonitoring.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a stored execution plan with compressed XML data.
/// </summary>
public sealed class ExecutionPlanEntry
{
    /// <summary>
    /// Unique identifier for this execution plan entry.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Query hash from sys.dm_exec_query_stats - used to link to Prometheus metrics.
    /// Format: 0x followed by hex string (e.g., 0x1234ABCD...)
    /// </summary>
    [MaxLength(66)]
    public string QueryHash { get; set; } = string.Empty;

    /// <summary>
    /// Connection ID that this plan was captured from.
    /// </summary>
    [MaxLength(16)]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// GZip compressed execution plan XML.
    /// Typically achieves 80-90% compression ratio.
    /// </summary>
    public byte[] CompressedPlanXml { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Original (uncompressed) size of the execution plan XML in bytes.
    /// </summary>
    public int OriginalSizeBytes { get; set; }

    /// <summary>
    /// Query text for reference and display.
    /// </summary>
    [MaxLength(4000)]
    public string QueryText { get; set; } = string.Empty;

    /// <summary>
    /// When this execution plan was captured.
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response DTO for execution plan retrieval.
/// </summary>
public sealed class ExecutionPlanResponse
{
    public string QueryHash { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string ExecutionPlanXml { get; set; } = string.Empty;
    public int OriginalSizeBytes { get; set; }
    public int CompressedSizeBytes { get; set; }
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Configuration for execution plan collection.
/// </summary>
public sealed class ExecutionPlanConfig
{
    /// <summary>
    /// Whether execution plan collection is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of days to retain execution plans before auto-pruning.
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// Maximum storage size in MB for execution plans.
    /// When exceeded, oldest plans are pruned.
    /// </summary>
    public int MaxStorageMB { get; set; } = 100;
}
