using Microsoft.Data.SqlClient;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Monitors SQL Server health and status.
/// 
/// Features:
/// - Connection count monitoring
/// - CPU and memory usage
/// - Server information and uptime
/// </summary>
public sealed class ServerHealthService : BaseMonitoringService
{
    #region SQL Queries
    
    private const string ServerInfoQuery = @"
        SELECT 
            @@SERVERNAME AS ServerName,
            @@VERSION AS SqlVersion,
            CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) AS Edition,
            (SELECT sqlserver_start_time FROM sys.dm_os_sys_info WITH (NOLOCK)) AS StartTime";

    private const string ConnectionCountQuery = @"
        SELECT COUNT(*) 
        FROM sys.dm_exec_sessions WITH (NOLOCK) 
        WHERE is_user_process = 1";

    private const string BlockedCountQuery = @"
        SELECT COUNT(*) 
        FROM sys.dm_exec_requests WITH (NOLOCK) 
        WHERE blocking_session_id <> 0";

    private const string MemoryQuery = @"
        SELECT 
            (physical_memory_in_use_kb / 1024) AS MemoryUsedMB,
            (
                SELECT 
                    CASE WHEN b.cntr_value = 0 THEN 0 
                    ELSE CAST(a.cntr_value * 100.0 / b.cntr_value AS BIGINT) END
                FROM sys.dm_os_performance_counters a WITH (NOLOCK)
                JOIN sys.dm_os_performance_counters b WITH (NOLOCK) ON a.object_name = b.object_name
                WHERE a.counter_name = 'Buffer cache hit ratio'
                  AND b.counter_name = 'Buffer cache hit ratio base'
                  AND a.object_name LIKE '%Buffer Manager%'
            ) AS BufferCacheHitRatio
        FROM sys.dm_os_process_memory WITH (NOLOCK)";
    
    #endregion

    #region Constructor
    
    public ServerHealthService(
        ConnectionService connectionService,
        ILogger<ServerHealthService> logger)
        : base(connectionService, logger)
    {
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets comprehensive server health status.
    /// </summary>
    public async Task<ServerHealth> GetServerHealthAsync()
    {
        var health = new ServerHealth();
        var connectionString = ConnectionService.GetConnectionString();

        // Check if connection is configured
        if (string.IsNullOrEmpty(connectionString))
        {
            health.IsConnected = false;
            health.ErrorMessage = "No connection configured. Please configure connection settings.";
            return health;
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            health.IsConnected = true;

            // Gather health metrics (all queries are low-impact with NOLOCK)
            await LoadServerInfoAsync(connection, health);
            await LoadConnectionStatsAsync(connection, health);
            await LoadMemoryStatsAsync(connection, health);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            // Timeout - server is under heavy load
            health.IsConnected = true;
            health.ErrorMessage = "Server is under heavy load (query timeout)";
        }
        catch (SqlException ex)
        {
            health.IsConnected = false;
            health.ErrorMessage = $"SQL Error: {ex.Message}";
            Logger.LogWarning(ex, "Failed to get server health");
        }
        catch (Exception ex)
        {
            health.IsConnected = false;
            health.ErrorMessage = $"Error: {ex.Message}";
            Logger.LogError(ex, "Unexpected error getting server health");
        }

        return health;
    }
    
    #endregion

    #region Private Methods
    
    private async Task LoadServerInfoAsync(SqlConnection connection, ServerHealth health)
    {
        try
        {
            await using var command = new SqlCommand(ServerInfoQuery, connection)
            {
                CommandTimeout = MetricsConstants.DefaultQueryTimeoutSeconds
            };

            await using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                health.ServerName = ReadString(reader, 0, "Unknown");
                
                var fullVersion = ReadString(reader, 1);
                health.SqlServerVersion = ExtractFirstLine(fullVersion);
                
                health.Edition = ReadString(reader, 2, "Unknown");
                health.ServerStartTime = ReadDateTime(reader, 3);
                health.Uptime = DateTime.Now - health.ServerStartTime;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load server info");
        }
    }

    private async Task LoadConnectionStatsAsync(SqlConnection connection, ServerHealth health)
    {
        try
        {
            // Active connections
            await using (var cmd = new SqlCommand(ConnectionCountQuery, connection)
            {
                CommandTimeout = MetricsConstants.DefaultQueryTimeoutSeconds
            })
            {
                var result = await cmd.ExecuteScalarAsync();
                health.ActiveConnections = Convert.ToInt32(result ?? 0);
            }

            // Blocked processes
            await using (var cmd = new SqlCommand(BlockedCountQuery, connection)
            {
                CommandTimeout = MetricsConstants.DefaultQueryTimeoutSeconds
            })
            {
                var result = await cmd.ExecuteScalarAsync();
                health.BlockedProcesses = Convert.ToInt32(result ?? 0);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load connection stats");
        }
    }

    private async Task LoadMemoryStatsAsync(SqlConnection connection, ServerHealth health)
    {
        try
        {
            await using var command = new SqlCommand(MemoryQuery, connection)
            {
                CommandTimeout = MetricsConstants.DefaultQueryTimeoutSeconds
            };

            await using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                health.MemoryUsedMb = ReadInt64(reader, 0);
                health.BufferCacheHitRatio = ReadInt64(reader, 1);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load memory stats");
        }
    }

    private static string ExtractFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return "Unknown";
        
        var newlineIndex = text.IndexOf('\n');
        return newlineIndex > 0 ? text[..newlineIndex].Trim() : text;
    }
    
    #endregion
}
