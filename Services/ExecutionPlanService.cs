namespace PbSqlServerMonitoring.Services;

using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Models;

/// <summary>
/// Interface for execution plan storage and retrieval.
/// </summary>
public interface IExecutionPlanService
{
    /// <summary>
    /// Gets an execution plan by query hash and connection ID.
    /// </summary>
    Task<ExecutionPlanResponse?> GetPlanByHashAsync(string queryHash, string connectionId);

    /// <summary>
    /// Gets the most recent execution plan for a query hash (any connection).
    /// </summary>
    Task<ExecutionPlanResponse?> GetPlanByHashAsync(string queryHash);

    /// <summary>
    /// Stores an execution plan with GZip compression.
    /// </summary>
    Task StorePlanAsync(string queryHash, string connectionId, string planXml, string queryText);

    /// <summary>
    /// Prunes execution plans older than the retention period.
    /// Returns the number of plans deleted.
    /// </summary>
    Task<int> PruneOldPlansAsync();

    /// <summary>
    /// Checks storage usage and prunes if exceeding MaxStorageMB.
    /// </summary>
    Task EnforceStorageLimitAsync();
}

/// <summary>
/// Service for managing execution plan storage with compression.
/// </summary>
public sealed class ExecutionPlanService : IExecutionPlanService
{
    private readonly MonitorDbContext _dbContext;
    private readonly ExecutionPlanConfig _config;
    private readonly ILogger<ExecutionPlanService> _logger;

    public ExecutionPlanService(
        MonitorDbContext dbContext,
        IOptions<ExecutionPlanConfig> config,
        ILogger<ExecutionPlanService> logger)
    {
        _dbContext = dbContext;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<ExecutionPlanResponse?> GetPlanByHashAsync(string queryHash, string connectionId)
    {
        var entry = await _dbContext.Set<ExecutionPlanEntry>()
            .Where(e => e.QueryHash == queryHash && e.ConnectionId == connectionId)
            .OrderByDescending(e => e.CapturedAt)
            .FirstOrDefaultAsync();

        return entry != null ? MapToResponse(entry) : null;
    }

    public async Task<ExecutionPlanResponse?> GetPlanByHashAsync(string queryHash)
    {
        var entry = await _dbContext.Set<ExecutionPlanEntry>()
            .Where(e => e.QueryHash == queryHash)
            .OrderByDescending(e => e.CapturedAt)
            .FirstOrDefaultAsync();

        return entry != null ? MapToResponse(entry) : null;
    }

    public async Task StorePlanAsync(string queryHash, string connectionId, string planXml, string queryText)
    {
        if (!_config.Enabled)
        {
            _logger.LogDebug("Execution plan collection is disabled, skipping storage");
            return;
        }

        // Check if we already have this exact plan (by hash and connection) captured recently
        var existingPlan = await _dbContext.Set<ExecutionPlanEntry>()
            .Where(e => e.QueryHash == queryHash && e.ConnectionId == connectionId)
            .OrderByDescending(e => e.CapturedAt)
            .FirstOrDefaultAsync();

        // Skip if we captured this plan within the last hour
        if (existingPlan != null && existingPlan.CapturedAt > DateTime.UtcNow.AddHours(-1))
        {
            _logger.LogDebug("Execution plan for {QueryHash} already captured recently, skipping", queryHash);
            return;
        }

        var compressed = CompressString(planXml);
        var entry = new ExecutionPlanEntry
        {
            QueryHash = queryHash,
            ConnectionId = connectionId,
            CompressedPlanXml = compressed,
            OriginalSizeBytes = System.Text.Encoding.UTF8.GetByteCount(planXml),
            QueryText = queryText.Length > 4000 ? queryText[..4000] : queryText,
            CapturedAt = DateTime.UtcNow
        };

        _dbContext.Set<ExecutionPlanEntry>().Add(entry);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Stored execution plan for {QueryHash}: {OriginalSize} bytes -> {CompressedSize} bytes ({Ratio:P0} reduction)",
            queryHash,
            entry.OriginalSizeBytes,
            compressed.Length,
            1.0 - (double)compressed.Length / entry.OriginalSizeBytes);
    }

    public async Task<int> PruneOldPlansAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_config.RetentionDays);
        
        var oldPlans = await _dbContext.Set<ExecutionPlanEntry>()
            .Where(e => e.CapturedAt < cutoffDate)
            .ToListAsync();

        if (oldPlans.Count > 0)
        {
            _dbContext.Set<ExecutionPlanEntry>().RemoveRange(oldPlans);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Pruned {Count} execution plans older than {CutoffDate}", oldPlans.Count, cutoffDate);
        }

        return oldPlans.Count;
    }

    public async Task EnforceStorageLimitAsync()
    {
        var maxBytes = _config.MaxStorageMB * 1024 * 1024;
        
        // Get total compressed size
        var totalSize = await _dbContext.Set<ExecutionPlanEntry>()
            .SumAsync(e => (long)e.CompressedPlanXml.Length);

        if (totalSize <= maxBytes)
        {
            return;
        }

        _logger.LogWarning("Execution plan storage ({CurrentMB:F1} MB) exceeds limit ({MaxMB} MB), pruning oldest entries",
            totalSize / (1024.0 * 1024.0), _config.MaxStorageMB);

        // Delete oldest plans until under limit
        var plansToDelete = await _dbContext.Set<ExecutionPlanEntry>()
            .OrderBy(e => e.CapturedAt)
            .Take(100) // Delete in batches
            .ToListAsync();

        _dbContext.Set<ExecutionPlanEntry>().RemoveRange(plansToDelete);
        await _dbContext.SaveChangesAsync();
    }

    private ExecutionPlanResponse MapToResponse(ExecutionPlanEntry entry)
    {
        return new ExecutionPlanResponse
        {
            QueryHash = entry.QueryHash,
            ConnectionId = entry.ConnectionId,
            QueryText = entry.QueryText,
            ExecutionPlanXml = DecompressString(entry.CompressedPlanXml),
            OriginalSizeBytes = entry.OriginalSizeBytes,
            CompressedSizeBytes = entry.CompressedPlanXml.Length,
            CapturedAt = entry.CapturedAt
        };
    }

    private static byte[] CompressString(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return memoryStream.ToArray();
    }

    private static string DecompressString(byte[] compressedBytes)
    {
        using var memoryStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
