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
    private readonly IMultiConnectionService _connectionService;
    private readonly IPrometheusTargetExporter _prometheusExporter;
    private readonly ILogger<ConnectionHealthMonitor> _logger;
    private readonly TimeSpan _checkInterval;

    public ConnectionHealthMonitor(
        IMultiConnectionService connectionService,
        IPrometheusTargetExporter prometheusExporter,
        ILogger<ConnectionHealthMonitor> logger,
        IConfiguration configuration)
    {
        _connectionService = connectionService;
        _prometheusExporter = prometheusExporter;
        _logger = logger;

        var intervalSeconds = configuration.GetValue("Monitoring:HealthCheckIntervalSeconds", 60);
        _checkInterval = TimeSpan.FromSeconds(Math.Max(30, intervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connection health monitor started. Checking every {Interval}s",
            _checkInterval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        try
        {
            await _prometheusExporter.ExportTargetsAsync(stoppingToken);
            _logger.LogInformation("Initial Prometheus targets export completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export Prometheus targets on startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllConnectionsAsync(stoppingToken);

                var cleanedUp = await _connectionService.CleanupStaleReauthConnectionsAsync(cancellationToken: stoppingToken);
                if (cleanedUp > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} stale connections requiring reauthentication", cleanedUp);
                }

                // Ensure running exporter config matches current connection states (e.g. if disconnected)
                await _prometheusExporter.ExportTargetsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
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

            // Skip manually disconnected connections (prevents auto-reconnect)
            if (connection.Status == ConnectionStatus.Disconnected)
            {
                continue;
            }

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
