using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Detects blocking sessions and analyzes locks.
/// 
/// Features:
/// - Identifies blocking chains
/// - Finds lead blockers
/// - Monitors current locks
/// </summary>
public sealed class BlockingService : BaseMonitoringService
{
    #region SQL Queries
    
    private const string BlockingSessionsQuery = @"
        ;WITH BlockingTree AS (
            SELECT 
                r.session_id,
                r.blocking_session_id,
                r.status,
                r.wait_type,
                r.wait_time,
                r.wait_resource,
                DB_NAME(r.database_id) AS database_name,
                s.host_name,
                s.program_name,
                s.login_name,
                r.cpu_time,
                r.logical_reads,
                s.memory_usage * 8 AS memory_usage_kb,
                r.start_time,
                st.text AS query_text,
                r.plan_handle
            FROM sys.dm_exec_requests r WITH (NOLOCK)
            INNER JOIN sys.dm_exec_sessions s WITH (NOLOCK) 
                ON r.session_id = s.session_id
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
            WHERE r.blocking_session_id <> 0
               OR r.session_id IN (
                   SELECT DISTINCT blocking_session_id 
                   FROM sys.dm_exec_requests WITH (NOLOCK) 
                   WHERE blocking_session_id <> 0
               )
        )
        SELECT TOP 50
            bt.session_id,
            bt.blocking_session_id,
            ISNULL(bt.status, 'unknown') AS status,
            ISNULL(bt.wait_type, '') AS wait_type,
            ISNULL(bt.wait_time, 0) AS wait_time,
            ISNULL(bt.wait_resource, '') AS wait_resource,
            ISNULL(bt.database_name, 'Unknown') AS database_name,
            ISNULL(bt.query_text, '') AS query_text,
            ISNULL(bt.host_name, '') AS host_name,
            ISNULL(bt.program_name, '') AS program_name,
            ISNULL(bt.login_name, '') AS login_name,
            ISNULL(bt.cpu_time, 0) AS cpu_time,
            ISNULL(bt.logical_reads, 0) AS logical_reads,
            ISNULL(bt.memory_usage_kb, 0) AS memory_usage_kb,
            bt.start_time,
            CASE 
                WHEN bt.blocking_session_id = 0 
                  OR bt.blocking_session_id IS NULL 
                THEN 1 
                ELSE 0 
            END AS is_lead_blocker,
            (
                SELECT COUNT(*) 
                FROM sys.dm_exec_requests WITH (NOLOCK) 
                WHERE blocking_session_id = bt.session_id
            ) AS blocked_count,
            CONVERT(NVARCHAR(MAX), qp.query_plan) AS execution_plan
        FROM BlockingTree bt
        OUTER APPLY sys.dm_exec_query_plan(bt.plan_handle) qp
        ORDER BY is_lead_blocker DESC, blocked_count DESC";

    private const string CurrentLocksQuery = @"
        SELECT TOP 100
            CAST(tl.request_session_id AS INT) AS SessionId,
            ISNULL(DB_NAME(tl.resource_database_id), 'Unknown') AS DatabaseName,
            '' AS ObjectName,
            tl.resource_type AS ResourceType,
            tl.request_mode AS RequestMode,
            tl.request_status AS RequestStatus,
            COUNT(*) AS RequestCount,
            MAX(ISNULL(st.text, '')) AS QueryText,
            MAX(ISNULL(s.host_name, '')) AS HostName,
            MAX(ISNULL(s.login_name, '')) AS LoginName
        FROM sys.dm_tran_locks tl WITH (NOLOCK)
        LEFT JOIN sys.dm_exec_sessions s WITH (NOLOCK) 
            ON tl.request_session_id = s.session_id
        LEFT JOIN sys.dm_exec_requests r WITH (NOLOCK) 
            ON tl.request_session_id = r.session_id
        OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) st
        WHERE tl.request_session_id <> @@SPID
          AND tl.resource_type <> 'DATABASE'
        GROUP BY 
            tl.request_session_id,
            tl.resource_database_id,
            tl.resource_type,
            tl.request_mode,
            tl.request_status
        ORDER BY RequestCount DESC";
    
    #endregion

    #region Constructor
    
    public BlockingService(
        ConnectionService connectionService,
        ILogger<BlockingService> logger)
        : base(connectionService, logger)
    {
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets all currently blocking sessions.
    /// </summary>
    public Task<List<BlockingSession>> GetBlockingSessionsAsync(string? connectionString = null)
    {
        return ExecuteMonitoringQueryAsync(
            BlockingSessionsQuery,
            MapBlockingSession,
            connectionString: connectionString);
    }

    /// <summary>
    /// Gets current locks in the database.
    /// </summary>
    public Task<List<LockInfo>> GetCurrentLocksAsync(string? connectionString = null)
    {
        return ExecuteMonitoringQueryAsync(
            CurrentLocksQuery,
            MapLockInfo,
            connectionString: connectionString);
    }
    
    #endregion

    #region Private Methods
    
    private static BlockingSession MapBlockingSession(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        return new BlockingSession
        {
            SessionId = reader.GetInt16(0),
            BlockingSessionId = reader.IsDBNull(1) ? null : (int?)reader.GetInt16(1),
            Status = ReadString(reader, 2, "unknown"),
            WaitType = ReadString(reader, 3),
            WaitTimeMs = reader.GetInt32(4),
            WaitResource = ReadString(reader, 5),
            DatabaseName = ReadString(reader, 6, "Unknown"),
            QueryText = ReadString(reader, 7),
            HostName = ReadString(reader, 8),
            ProgramName = ReadString(reader, 9),
            LoginName = ReadString(reader, 10),
            CpuTime = reader.GetInt32(11),
            LogicalReads = ReadInt64(reader, 12),
            MemoryUsageKb = reader.GetInt32(13),
            StartTime = ReadNullableDateTime(reader, 14),
            IsLeadBlocker = reader.GetInt32(15) == 1,
            BlockedCount = reader.GetInt32(16),
            ExecutionPlan = ReadString(reader, 17)
        };
    }

    private static LockInfo MapLockInfo(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        return new LockInfo
        {
            SessionId = reader.GetInt32(0),
            DatabaseName = ReadString(reader, 1, "Unknown"),
            ObjectName = ReadString(reader, 2),
            ResourceType = ReadString(reader, 3),
            RequestMode = ReadString(reader, 4),
            RequestStatus = ReadString(reader, 5),
            RequestCount = reader.GetInt32(6),
            QueryText = ReadString(reader, 7),
            HostName = ReadString(reader, 8),
            LoginName = ReadString(reader, 9)
        };
    }
    
    #endregion
}
