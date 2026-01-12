using Microsoft.Data.SqlClient;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Monitors currently executing queries in SQL Server.
/// 
/// Features:
/// - Shows active queries in real-time
/// - Low-impact with NOLOCK hints
/// - Excludes system and monitoring queries
/// </summary>
public sealed class RunningQueriesService : BaseMonitoringService
{
    #region SQL Queries
    
    private const string RunningQueriesQuery = @"
        SELECT TOP 50
            r.session_id AS SessionId,
            ISNULL(DB_NAME(r.database_id), 'Unknown') AS DatabaseName,
            r.status AS Status,
            r.command AS Command,
            ISNULL(r.wait_type, '') AS WaitType,
            r.wait_time AS WaitTimeMs,
            r.cpu_time AS CpuTimeMs,
            r.total_elapsed_time AS ElapsedTimeMs,
            r.logical_reads AS LogicalReads,
            r.writes AS Writes,
            r.start_time AS StartTime,
            ISNULL(SUBSTRING(st.text, (r.statement_start_offset / 2) + 1,
                ((CASE r.statement_end_offset
                    WHEN -1 THEN DATALENGTH(st.text)
                    ELSE r.statement_end_offset
                END - r.statement_start_offset) / 2) + 1), '') AS QueryText,
            ISNULL(s.host_name, '') AS HostName,
            ISNULL(s.program_name, '') AS ProgramName,
            ISNULL(s.login_name, '') AS LoginName,
            r.percent_complete AS PercentComplete,
            r.blocking_session_id AS BlockingSessionId
        FROM sys.dm_exec_requests r WITH (NOLOCK)
        INNER JOIN sys.dm_exec_sessions s WITH (NOLOCK) 
            ON r.session_id = s.session_id
        CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
        WHERE r.session_id <> @@SPID
          AND s.is_user_process = 1
          AND r.status <> 'background'
          AND st.text NOT LIKE '%sys.dm_exec_requests%'
          AND st.text NOT LIKE '%dm_exec_sql_text%'
        ORDER BY r.total_elapsed_time DESC";
    
    #endregion

    #region Constructor
    
    public RunningQueriesService(
        ConnectionService connectionService,
        ILogger<RunningQueriesService> logger)
        : base(connectionService, logger)
    {
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets all currently running queries.
    /// </summary>
    public Task<List<RunningQuery>> GetRunningQueriesAsync()
    {
        return ExecuteMonitoringQueryAsync(
            RunningQueriesQuery,
            MapRunningQuery);
    }
    
    #endregion

    #region Private Methods
    
    private static RunningQuery MapRunningQuery(SqlDataReader reader)
    {
        return new RunningQuery
        {
            SessionId = reader.GetInt16(0),
            DatabaseName = ReadString(reader, 1, "Unknown"),
            Status = ReadString(reader, 2),
            Command = ReadString(reader, 3),
            WaitType = ReadString(reader, 4),
            WaitTimeMs = reader.GetInt32(5),
            CpuTimeMs = reader.GetInt32(6),
            ElapsedTimeMs = reader.GetInt32(7),
            LogicalReads = ReadInt64(reader, 8),
            Writes = ReadInt64(reader, 9),
            StartTime = ReadDateTime(reader, 10),
            QueryText = ReadString(reader, 11),
            HostName = ReadString(reader, 12),
            ProgramName = ReadString(reader, 13),
            LoginName = ReadString(reader, 14),
            PercentComplete = ReadDouble(reader, 15),
            BlockingSessionId = reader.IsDBNull(16) ? null : (int?)reader.GetInt16(16)
        };
    }
    
    #endregion
}
