using Microsoft.Data.SqlClient;
using PbSqlServerMonitoring.Configuration;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Base class for SQL monitoring services.
/// Provides common functionality:
/// - Low-impact query execution with READ UNCOMMITTED
/// - Query timeouts to prevent blocking
/// - Error handling with meaningful messages
/// </summary>
public abstract class BaseMonitoringService
{
    #region Fields
    
    protected readonly ConnectionService ConnectionService;
    protected readonly ILogger Logger;
    
    #endregion

    #region Constructor
    
    protected BaseMonitoringService(ConnectionService connectionService, ILogger logger)
    {
        ConnectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    #endregion

    #region Protected Methods
    
    /// <summary>
    /// Executes a monitoring query with low-impact settings:
    /// - READ UNCOMMITTED isolation (no locks)
    /// - Short query timeout
    /// - Automatic connection management
    /// </summary>
    protected async Task<List<T>> ExecuteMonitoringQueryAsync<T>(
        string sql,
        Func<SqlDataReader, T> mapper,
        Action<SqlCommand>? configureCommand = null,
        int? timeoutSeconds = null)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(mapper);
        
        var connectionString = ConnectionService.GetConnectionString();
        
        if (string.IsNullOrEmpty(connectionString))
        {
            Logger.LogDebug("No connection string configured, returning empty result");
            return [];
        }

        var results = new List<T>();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Set READ UNCOMMITTED to avoid locks on production data
            await SetReadUncommittedAsync(connection);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = timeoutSeconds ?? MetricsConstants.DefaultQueryTimeoutSeconds
            };

            configureCommand?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync() && results.Count < MetricsConstants.MaxTopN)
            {
                results.Add(mapper(reader));
            }
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            // Timeout - server is under pressure, return empty gracefully
            Logger.LogWarning("Query timed out, server may be under pressure");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing monitoring query");
            throw;
        }

        return results;
    }

    /// <summary>
    /// Executes a scalar query (single value) with low-impact settings.
    /// </summary>
    protected async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        int? timeoutSeconds = null)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(sql);
        
        var connectionString = ConnectionService.GetConnectionString();
        
        if (string.IsNullOrEmpty(connectionString))
        {
            return default;
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await SetReadUncommittedAsync(connection);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = timeoutSeconds ?? MetricsConstants.DefaultQueryTimeoutSeconds
            };

            var result = await command.ExecuteScalarAsync();
            
            if (result == null || result == DBNull.Value)
            {
                return default;
            }

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            Logger.LogWarning("Scalar query timed out");
            return default;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing scalar query");
            return default;
        }
    }

    /// <summary>
    /// Safely reads a string column, handling nulls.
    /// </summary>
    protected static string ReadString(SqlDataReader reader, int ordinal, string defaultValue = "")
    {
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetString(ordinal);
    }

    /// <summary>
    /// Safely reads a nullable DateTime column.
    /// </summary>
    protected static DateTime? ReadNullableDateTime(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    /// <summary>
    /// Safely reads a DateTime column with default value.
    /// </summary>
    protected static DateTime ReadDateTime(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
    }

    /// <summary>
    /// Safely reads an Int64, handling type variations.
    /// </summary>
    protected static long ReadInt64(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return 0;
        
        var type = reader.GetFieldType(ordinal);
        
        return type == typeof(int) 
            ? reader.GetInt32(ordinal) 
            : reader.GetInt64(ordinal);
    }

    /// <summary>
    /// Safely reads a Double/Decimal as double.
    /// </summary>
    protected static double ReadDouble(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return 0;
        
        var type = reader.GetFieldType(ordinal);
        
        if (type == typeof(decimal))
            return (double)reader.GetDecimal(ordinal);
        if (type == typeof(float))
            return reader.GetFloat(ordinal);
            
        return reader.GetDouble(ordinal);
    }
    
    #endregion

    #region Private Methods
    
    /// <summary>
    /// Sets the connection to READ UNCOMMITTED isolation level.
    /// This prevents the monitoring queries from taking locks.
    /// </summary>
    private static async Task SetReadUncommittedAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", 
            connection)
        {
            CommandTimeout = 5
        };
        
        await command.ExecuteNonQueryAsync();
    }
    
    #endregion
}
