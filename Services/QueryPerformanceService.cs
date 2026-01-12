using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Monitors SQL Server query performance using DMVs.
/// 
/// Features:
/// - Tracks CPU, IO, and execution time metrics
/// - Uses READ UNCOMMITTED to avoid blocking
/// - Low-impact queries with short timeouts
/// </summary>
public sealed class QueryPerformanceService : BaseMonitoringService
{
    #region SQL Queries
    
    /// <summary>
    /// Base query for performance metrics.
    /// Uses NOLOCK hint and filters out system queries.
    /// </summary>
    /// <summary>
    /// Base query for performance metrics.
    /// OPTIMIZED: Selects Top N from stats FIRST, then joins to text to avoid expensive function calls on all rows.
    /// </summary>
    /// <summary>
    /// Base query for performance metrics.
    /// OPTIMIZED: Uses derived table to sort/filter stats BEFORE joining to text.
    /// </summary>
    private const string BasePerformanceQuery = @"
        SELECT 
            CONVERT(VARCHAR(64), qs.query_hash, 1) AS QueryHash,
            SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                ((CASE qs.statement_end_offset
                    WHEN -1 THEN DATALENGTH(st.text)
                    ELSE qs.statement_end_offset
                END - qs.statement_start_offset) / 2) + 1) AS QueryText,
            ISNULL(DB_NAME(TRY_CAST(pa.value AS INT)), ISNULL(DB_NAME(st.dbid), 'Unknown')) AS DatabaseName,
            qs.execution_count AS ExecutionCount,
            qs.total_worker_time / 1000.0 AS TotalCpuTimeMs,
            CASE WHEN qs.execution_count > 0 
                 THEN (qs.total_worker_time / qs.execution_count) / 1000.0 
                 ELSE 0 END AS AvgCpuTimeMs,
            qs.total_elapsed_time / 1000.0 AS TotalElapsedTimeMs,
            CASE WHEN qs.execution_count > 0 
                 THEN (qs.total_elapsed_time / qs.execution_count) / 1000.0 
                 ELSE 0 END AS AvgElapsedTimeMs,
            qs.total_logical_reads AS TotalLogicalReads,
            CASE WHEN qs.execution_count > 0 
                 THEN qs.total_logical_reads / qs.execution_count 
                 ELSE 0 END AS AvgLogicalReads,
            qs.total_logical_writes AS TotalLogicalWrites,
            CASE WHEN qs.execution_count > 0 
                 THEN qs.total_logical_writes / qs.execution_count 
                 ELSE 0 END AS AvgLogicalWrites,
            qs.total_physical_reads AS TotalPhysicalReads,
            CASE WHEN qs.execution_count > 0 
                 THEN qs.total_physical_reads / qs.execution_count 
                 ELSE 0 END AS AvgPhysicalReads,
            qs.last_execution_time AS LastExecutionTime,
            qs.creation_time AS CreationTime
        FROM (
            SELECT TOP (@TopN)
                qs.plan_handle,
                qs.sql_handle,
                qs.query_hash,
                qs.statement_start_offset,
                qs.statement_end_offset,
                qs.execution_count,
                qs.total_worker_time,
                qs.total_elapsed_time,
                qs.total_logical_reads,
                qs.total_logical_writes,
                qs.total_physical_reads,
                qs.last_execution_time,
                qs.creation_time
            FROM sys.dm_exec_query_stats qs WITH (NOLOCK)
            WHERE qs.execution_count > 0
            AND qs.last_execution_time > DATEADD(hour, -1, GETDATE())
            {ORDER_BY_CLAUSE}
        ) AS qs
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
        OUTER APPLY sys.dm_exec_plan_attributes(qs.plan_handle) pa
        WHERE pa.attribute = 'dbid'
          AND st.text NOT LIKE '%sys.dm_%'
          AND st.text NOT LIKE '%FETCH API_CURSOR%'";
    
    // NOTE: Removed 'qs.' prefix from ORDER BY columns as they are resolved in the subquery scope
    private const string OrderByCpu = "ORDER BY total_worker_time DESC";
    private const string OrderByIo = "ORDER BY total_logical_reads DESC";
    private const string OrderByDuration = "ORDER BY (total_elapsed_time / execution_count) DESC";
    
    #endregion

    #region Constructor
    
    public QueryPerformanceService(
        ConnectionService connectionService,
        ILogger<QueryPerformanceService> logger)
        : base(connectionService, logger)
    {
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets top queries by total CPU time.
    /// </summary>
    /// <param name="topN">Number of results (max 100)</param>
    public Task<List<QueryPerformance>> GetTopCpuQueriesAsync(int topN = 25)
    {
        var sql = BasePerformanceQuery.Replace("{ORDER_BY_CLAUSE}", OrderByCpu);
        return ExecutePerformanceQueryAsync(sql, ClampTopN(topN));
    }

    /// <summary>
    /// Gets top queries by logical reads (IO).
    /// </summary>
    /// <param name="topN">Number of results (max 100)</param>
    public Task<List<QueryPerformance>> GetTopIoQueriesAsync(int topN = 25)
    {
        var sql = BasePerformanceQuery.Replace("{ORDER_BY_CLAUSE}", OrderByIo);
        return ExecutePerformanceQueryAsync(sql, ClampTopN(topN));
    }

    /// <summary>
    /// Gets top queries by active CPU time (currently running requests).
    /// </summary>
    /// <param name="topN">Number of results (max 100)</param>
    public Task<List<QueryPerformance>> GetActiveHighCpuQueriesAsync(int topN = 5)
    {
        const string sql = @"
            SELECT TOP (@TopN)
                CONVERT(VARCHAR(64), r.query_hash, 1) AS QueryHash,
                SUBSTRING(t.text, (r.statement_start_offset/2) + 1,
                    ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text) ELSE r.statement_end_offset END - r.statement_start_offset)/2) + 1) AS QueryText,
                ISNULL(DB_NAME(r.database_id), 'Unknown') AS DatabaseName,
                1 AS ExecutionCount,
                CAST(r.cpu_time AS FLOAT) AS TotalCpuTimeMs,
                CAST(r.cpu_time AS FLOAT) AS AvgCpuTimeMs,
                CAST(r.total_elapsed_time AS FLOAT) AS TotalElapsedTimeMs,
                CAST(r.total_elapsed_time AS FLOAT) AS AvgElapsedTimeMs,
                r.logical_reads AS TotalLogicalReads,
                r.logical_reads AS AvgLogicalReads,
                r.writes AS TotalLogicalWrites,
                r.writes AS AvgLogicalWrites,
                r.reads AS TotalPhysicalReads,
                r.reads AS AvgPhysicalReads,
                r.start_time AS LastExecutionTime,
                r.start_time AS CreationTime
            FROM sys.dm_exec_requests r WITH (NOLOCK)
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
            WHERE r.session_id > 50 
              AND r.session_id <> @@SPID
              AND r.cpu_time > 0
            ORDER BY r.cpu_time DESC";

        return ExecutePerformanceQueryAsync(sql, ClampTopN(topN));
    }

    /// <summary>
    /// Gets slowest queries by average elapsed time.
    /// </summary>
    /// <param name="topN">Number of results (max 100)</param>
    public Task<List<QueryPerformance>> GetSlowestQueriesAsync(int topN = 25)
    {
        var sql = BasePerformanceQuery.Replace("{ORDER_BY_CLAUSE}", OrderByDuration);
        return ExecutePerformanceQueryAsync(sql, ClampTopN(topN));
    }
    
    #endregion

    #region Private Methods
    
    private Task<List<QueryPerformance>> ExecutePerformanceQueryAsync(string sql, int topN)
    {
        return ExecuteMonitoringQueryAsync(
            sql,
            MapQueryPerformance,
            cmd => 
            {
                cmd.Parameters.AddWithValue("@TopN", topN);
            },
            timeoutSeconds: 30); // Explicit 30s timeout for heavy query reports
    }

    private static QueryPerformance MapQueryPerformance(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        return new QueryPerformance
        {
            QueryHash = ReadString(reader, 0),
            QueryText = ReadString(reader, 1, "[Unable to retrieve query text]"),
            DatabaseName = ReadString(reader, 2, "Unknown"),
            ExecutionCount = ReadInt64(reader, 3),
            TotalCpuTimeMs = ReadDouble(reader, 4),
            AvgCpuTimeMs = ReadDouble(reader, 5),
            TotalElapsedTimeMs = ReadDouble(reader, 6),
            AvgElapsedTimeMs = ReadDouble(reader, 7),
            TotalLogicalReads = ReadInt64(reader, 8),
            AvgLogicalReads = ReadInt64(reader, 9),
            TotalLogicalWrites = ReadInt64(reader, 10),
            AvgLogicalWrites = ReadInt64(reader, 11),
            TotalPhysicalReads = ReadInt64(reader, 12),
            AvgPhysicalReads = ReadInt64(reader, 13),
            LastExecutionTime = ReadDateTime(reader, 14),
            CreationTime = ReadDateTime(reader, 15)
        };
    }

    private static int ClampTopN(int topN)
    {
        return Math.Clamp(topN, 1, MetricsConstants.MaxTopN);
    }
    
    #endregion
}
