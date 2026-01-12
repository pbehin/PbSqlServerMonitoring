using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Interface for managing multiple SQL Server connections.
/// </summary>
public interface IMultiConnectionService
{
    /// <summary>
    /// Gets the maximum number of allowed connections.
    /// </summary>
    int MaxConnections { get; }
    
    /// <summary>
    /// Gets the current number of configured connections.
    /// </summary>
    int ConnectionCount { get; }
    
    /// <summary>
    /// Gets all configured connections (without sensitive data).
    /// </summary>
    IReadOnlyList<ServerConnection> Connections { get; }
    
    /// <summary>
    /// Adds a new SQL Server connection configuration.
    /// </summary>
    Task<(bool Success, string Message, ServerConnection? Connection)> AddConnectionAsync(
        AddConnectionRequest request, 
        string? userId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a connection by ID.
    /// </summary>
    Task<(bool Success, string Message)> RemoveConnectionAsync(
        string connectionId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Enables or disables a connection.
    /// </summary>
    Task<(bool Success, string Message)> SetConnectionEnabledAsync(
        string connectionId, 
        bool enabled,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets connection info by ID.
    /// </summary>
    ServerConnection? GetConnection(string connectionId);
    
    /// <summary>
    /// Gets all enabled connections.
    /// </summary>
    IReadOnlyList<ServerConnection> GetEnabledConnections();
    
    /// <summary>
    /// Gets the decrypted connection string for a given connection ID.
    /// </summary>
    string? GetConnectionString(string connectionId);
    
    /// <summary>
    /// Gets the status of all connections.
    /// </summary>
    MultiConnectionStatusResponse GetStatus();
    
    /// <summary>
    /// Tests a stored connection.
    /// </summary>
    Task<(bool Success, string Message)> TestStoredConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Tests all enabled connections.
    /// </summary>
    Task TestAllConnectionsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects a connection - stops data collection.
    /// </summary>
    Task<(bool Success, string Message)> DisconnectConnectionAsync(
        string connectionId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates the connection status.
    /// </summary>
    Task UpdateConnectionStatusAsync(string connectionId, ConnectionStatus status, string? errorMessage = null);
    
    /// <summary>
    /// Updates credentials for an existing connection (e.g., after encryption key loss).
    /// </summary>
    Task<(bool Success, string Message)> UpdateConnectionCredentialsAsync(
        string connectionId,
        string? username,
        string? password,
        bool useWindowsAuth,
        CancellationToken cancellationToken = default);
}
