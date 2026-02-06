using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Http;
using System.Text.Json;
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
            
            // Rewrite connection string if TrustServerCertificate is true (proxy candidate)
            var effectiveConnString = rawConn;
            try 
            {
               var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(rawConn);
               if (builder.TrustServerCertificate)
               {
                   _logger.LogInformation("Connection '{Name}' uses independent certificate trust. Registering via TLS Proxy.", connection.Name);
                   
                   // Extract Host/Port
                   var host = builder.DataSource;
                   int port = 1433;

                   if (host.Contains(","))
                   {
                       var parts = host.Split(',');
                       host = parts[0].Trim();
                       if (int.TryParse(parts[1], out var p)) port = p;
                   }
                   else if (host.Contains(":"))
                   {
                       var parts = host.Split(':');
                       host = parts[0].Trim();
                       if (int.TryParse(parts[1], out var p)) port = p;
                   }

                   // Register with Proxy API
                   var proxyPort = await RegisterProxyTargetAsync(host, port, cancellationToken);
                   
                   if (proxyPort.HasValue)
                   {
                        builder.DataSource = $"sql_proxy,{proxyPort.Value}"; 
                        builder.TrustServerCertificate = true;
                        builder.Encrypt = true;
                        effectiveConnString = builder.ConnectionString;
                   }
                   else
                   {
                       _logger.LogWarning("Failed to register proxy for connection '{Name}'. Falling back to direct.", connection.Name);
                   }
               }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse connection string for proxy check. Using original.");
            }

            sb.AppendLine($"  - job_name: {jobName}");
            sb.AppendLine($"    collectors: [mssql_standard]");
            sb.AppendLine("    static_configs:");
            sb.AppendLine("      - targets:");
            sb.AppendLine($"          {connection.Name.Replace(" ", "_")}: '{FormatConnectionString(effectiveConnString ?? "")}'");
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

    private async Task<int?> RegisterProxyTargetAsync(string targetHost, int targetPort, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var proxyUrl = _configuration.GetValue<string>("SqlExporter:ProxyManagementUrl") ?? "http://localhost:8085";
            
            var payload = new { targetHost, targetPort };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload), 
                Encoding.UTF8, 
                "application/json");

            var response = await client.PostAsync($"{proxyUrl}/register", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("port", out var portProp))
            {
                return portProp.GetInt32();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register target {Host}:{Port} with proxy", targetHost, targetPort);
            return null;
        }
    }

    private string FormatConnectionString(string connString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connString);
            var host = builder.DataSource;
            var port = "1433";

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

            // Ensure TrustServerCertificate is correctly propagated as CamelCase for go-mssqldb
            if (builder.TrustServerCertificate) uri += "&TrustServerCertificate=true";
            else uri += "&TrustServerCertificate=false";

            if (builder.ConnectTimeout > 0) uri += $"&connection+timeout={builder.ConnectTimeout}";

            return uri;
        }
        catch
        {
            return connString.Replace(",", ":");
        }
    }
}
