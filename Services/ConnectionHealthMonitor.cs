using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Background service that periodically tests all configured connections
/// and updates their status. Ensures all connections remain healthy.
/// 
/// Features:
/// - Tests all enabled connections every 60 seconds (configurable)
/// - Updates connection status in real-time
/// - Logs connection failures
/// </summary>
public sealed class ConnectionHealthMonitor : BackgroundService
{
    private readonly MultiConnectionService _connectionService;
    private readonly ILogger<ConnectionHealthMonitor> _logger;
    private readonly TimeSpan _checkInterval;

    public ConnectionHealthMonitor(
        MultiConnectionService connectionService,
        ILogger<ConnectionHealthMonitor> logger,
        IConfiguration configuration)
    {
        _connectionService = connectionService;
        _logger = logger;
        
        // Default 60 seconds, configurable via settings
        var intervalSeconds = configuration.GetValue("Monitoring:HealthCheckIntervalSeconds", 60);
        _checkInterval = TimeSpan.FromSeconds(Math.Max(30, intervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connection health monitor started. Checking every {Interval}s", 
            _checkInterval.TotalSeconds);

        // Wait a bit for the app to fully start before first check
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllConnectionsAsync(stoppingToken);
                
                // Cleanup connections that have required reauthentication for too long (24 hours default)
                var cleanedUp = await _connectionService.CleanupStaleReauthConnectionsAsync(cancellationToken: stoppingToken);
                if (cleanedUp > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} stale connections requiring reauthentication", cleanedUp);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during connection health check");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Connection health monitor stopped.");
    }

    private async Task CheckAllConnectionsAsync(CancellationToken stoppingToken)
    {
        var connections = _connectionService.GetEnabledConnections();
        
        if (connections.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Checking health of {Count} connections...", connections.Count);

        var healthyCount = 0;
        var failedCount = 0;

        foreach (var connection in connections)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var result = await _connectionService.TestStoredConnectionAsync(connection.Id, stoppingToken);
                
                if (result.Success)
                {
                    healthyCount++;
                }
                else
                {
                    failedCount++;
                    _logger.LogWarning(
                        "Connection {Name} ({Server}) is unhealthy: {Error}",
                        connection.Name, connection.Server, result.Message);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                await _connectionService.UpdateConnectionStatusAsync(
                    connection.Id, 
                    ConnectionStatus.Error, 
                    ex.Message);
                
                _logger.LogWarning(ex, 
                    "Failed to check connection {Name} ({Server})", 
                    connection.Name, connection.Server);
            }
        }

        _logger.LogDebug(
            "Connection health check complete: {Healthy}/{Total} healthy", 
            healthyCount, connections.Count);
    }
}
