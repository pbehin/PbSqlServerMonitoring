using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

public interface IPrometheusTargetExporter
{
    Task ExportTargetsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exports SQL Server connection targets to sql_exporter configuration.
/// Automatically reloads sql_exporter when targets change.
/// </summary>
public sealed class PrometheusTargetExporter : IPrometheusTargetExporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PrometheusTargetExporter> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PrometheusTargetExporter(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PrometheusTargetExporter> logger,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        _configFilePath = configuration["SqlExporter:ConfigPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "monitoring", "sql_exporter", "sql_exporter.yml");

        _logger.LogInformation("Prometheus Target Exporter initialized.");
    }

    public async Task ExportTargetsAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var connectionService = scope.ServiceProvider.GetRequiredService<IMultiConnectionService>();
            
            // Only export connections that are Enabled AND NOT Disconnected
            var enabledConnections = connectionService.GetEnabledConnections()
                .Where(c => c.Status != ConnectionStatus.Disconnected)
                .ToList();

            await WriteSqlExporterConfigAsync(enabledConnections, connectionService, cancellationToken);
            await ReloadSqlExporterAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export targets");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteSqlExporterConfigAsync(
        IReadOnlyList<ServerConnection> connections,
        IMultiConnectionService connectionService,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Auto-generated sql_exporter configuration");
        sb.AppendLine("global:");
        sb.AppendLine("  scrape_timeout: 10s");
        sb.AppendLine("  min_interval: 5s");
        sb.AppendLine();
        sb.AppendLine("collector_files:");
        sb.AppendLine("  - /etc/sql_exporter/mssql_standard.collector.yml");
        sb.AppendLine();
        sb.AppendLine("jobs:");

        if (!connections.Any())
        {
            // sql_exporter requires at least one job, use a dummy that won't connect
            sb.AppendLine("  - job_name: default");
            sb.AppendLine("    collectors: [mssql_standard]");
            sb.AppendLine("    static_configs:");
            sb.AppendLine("      - targets:");
            var dummyConn = _configuration["SqlExporter:DummyConnectionString"] ?? "sqlserver://sa:dummy@dummy-host:1433";
            sb.AppendLine($"          dummy: '{dummyConn}'");
            sb.AppendLine("        labels:");
            sb.AppendLine("          connection_id: '0000000000000000'");
            sb.AppendLine("          connection_name: 'Default Dummy'");
            sb.AppendLine("          server: 'dummy-host'");
            sb.AppendLine("          database: 'master'");
            sb.AppendLine("          user_id: 'system'");
            sb.AppendLine();
        }

        foreach (var connection in connections)
        {
            var jobName = $"conn_{connection.Id.ToLowerInvariant()}";
            var rawConn = connectionService.GetConnectionString(connection.Id);

            sb.AppendLine($"  - job_name: {jobName}");
            sb.AppendLine($"    collectors: [mssql_standard]");
            sb.AppendLine("    static_configs:");
            sb.AppendLine("      - targets:");
            sb.AppendLine($"          {connection.Name.Replace(" ", "_")}: '{FormatConnectionString(rawConn ?? "")}'");
            sb.AppendLine("        labels:");
            sb.AppendLine($"          connection_id: '{connection.Id}'");
            sb.AppendLine($"          connection_name: '{connection.Name}'");
            sb.AppendLine($"          server: '{connection.Server}'");
            sb.AppendLine($"          database: '{connection.Database}'");
            sb.AppendLine($"          user_id: '{connection.UserId}'");
            sb.AppendLine();
        }

        var directory = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_configFilePath, sb.ToString(), cancellationToken);
        _logger.LogInformation("Exported {Count} targets to sql_exporter config at {Path}", connections.Count, _configFilePath);
    }

    private async Task ReloadSqlExporterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var reloadUrl = _configuration.GetValue<string>("SqlExporter:ReloadUrl") 
                ?? throw new InvalidOperationException("SqlExporter:ReloadUrl configuration is missing");
            var response = await client.PostAsync(reloadUrl, null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully reloaded sql_exporter");
            }
            else
            {
                _logger.LogError("Failed to reload sql_exporter: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to sql_exporter for reload");
        }
    }

    /// <summary>
    /// Formats a connection string as a sql_exporter URI.
    /// Handles localhost -> host.containers.internal conversion for Docker.
    /// </summary>
    private string FormatConnectionString(string connString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connString);
            var host = builder.DataSource;
            var port = "1433";

            // Parse host:port or host,port
            if (host.Contains(","))
            {
                var parts = host.Split(',');
                host = parts[0].Trim();
                if (parts.Length > 1) port = parts[1].Trim();
            }
            else if (host.Contains(":"))
            {
                var parts = host.Split(':');
                host = parts[0].Trim();
                if (parts.Length > 1) port = parts[1].Trim();
            }

            // Convert localhost to Docker-accessible address
            if (host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("127.0.0.1") ||
                host == ".")
            {
                host = "host.containers.internal";
            }

            var user = Uri.EscapeDataString(builder.UserID);
            var pass = Uri.EscapeDataString(builder.Password);
            var db = Uri.EscapeDataString(builder.InitialCatalog);

            var uri = $"sqlserver://{user}:{pass}@{host}:{port}?database={db}";

            if (builder.Encrypt) uri += "&encrypt=true";
            else uri += "&encrypt=false";

            if (builder.TrustServerCertificate) uri += "&trustservercertificate=true";

            if (builder.ConnectTimeout > 0) uri += $"&connection+timeout={builder.ConnectTimeout}";

            return uri;
        }
        catch
        {
            return connString.Replace(",", ":");
        }
    }
}
