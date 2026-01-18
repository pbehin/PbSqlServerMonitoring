using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Manages multiple SQL Server connections with secure database storage.
///
/// Features:
/// - Add/remove/update connections
/// - Secure connection string storage using Data Protection API
/// - Connection status monitoring for all configured connections
/// - Configurable connection limit
///
/// Security:
/// - Passwords encrypted at rest in database
/// - Connection strings never logged
/// - Sensitive data masked in responses
/// </summary>
public sealed class MultiConnectionService : IMultiConnectionService, IDisposable
{
    private const string ProtectionPurpose = "PbSqlServerMonitoring.MultiConnections.v1";
    private const int DefaultMaxConnections = 5;

    private readonly IDataProtector _protector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MultiConnectionService> _logger;
    private readonly IConfiguration _configuration;

    private readonly IPrometheusTargetExporter _prometheusExporter;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly int _maxConnections;



    public MultiConnectionService(
        IDataProtectionProvider dataProtection,
        IServiceScopeFactory scopeFactory,
        ILogger<MultiConnectionService> logger,
        IConfiguration configuration,
        IPrometheusTargetExporter prometheusExporter)
    {
        _protector = dataProtection.CreateProtector(ProtectionPurpose);
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _prometheusExporter = prometheusExporter;

        _maxConnections = _configuration.GetValue(
            "Monitoring:MaxConnections",
            int.TryParse(Environment.GetEnvironmentVariable("PB_MONITOR_MAX_CONNECTIONS"), out var envMax)
                ? envMax
                : DefaultMaxConnections);
    }



    public int MaxConnections => _maxConnections;

    public int ConnectionCount
    {
        get
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            return dbContext.ServerConnections.Count();
        }
    }

    public IReadOnlyList<ServerConnection> Connections
    {
        get
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            return dbContext.ServerConnections.AsNoTracking().ToList().AsReadOnly();
        }
    }



    public async Task<(bool Success, string Message, ServerConnection? Connection)> AddConnectionAsync(
        AddConnectionRequest request,
        string? userId = null,
        int? userMaxConnections = null,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var effectiveMax = userMaxConnections ?? _configuration.GetValue("Monitoring:DefaultUserMaxConnections", 2);
            var userConnectionCount = await dbContext.ServerConnections
                .CountAsync(c => c.UserId == userId, cancellationToken);

            if (userConnectionCount >= effectiveMax)
            {
                return (false, $"You have reached your connection limit of {effectiveMax}. Remove a connection first.", null);
            }

            var (serverValid, sanitizedServer, serverError) = InputValidationExtensions.ValidateServerName(request.Server);
            if (!serverValid) return (false, serverError!, null);

            var (dbValid, sanitizedDb, dbError) = InputValidationExtensions.ValidateDatabaseName(request.Database);
            if (!dbValid) return (false, dbError!, null);

            if (!request.UseWindowsAuth)
            {
                var (userValid, sanitizedUser, userError) = InputValidationExtensions.ValidateUsername(request.Username);
                if (!userValid) return (false, userError!, null);
            }

            var exists = await dbContext.ServerConnections.AnyAsync(c =>
                c.UserId == userId &&
                c.Server.ToLower() == request.Server.ToLower() &&
                c.Database.ToLower() == request.Database.ToLower(), cancellationToken);

            if (exists)
            {
                return (false, $"A connection to {request.Server}/{request.Database} already exists.", null);
            }

            var connectionString = BuildConnectionString(request);
            var testResult = await TestConnectionAsync(connectionString);
            if (!testResult.Success) return (false, $"Connection test failed: {testResult.Message}", null);

            var connection = new ServerConnection
            {
                Id = Guid.NewGuid().ToString("N")[..16],
                UserId = userId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? $"{request.Server}/{request.Database}" : request.Name,
                Server = request.Server,
                Database = request.Database ?? "master",
                UseWindowsAuth = request.UseWindowsAuth,
                Username = request.UseWindowsAuth ? null : request.Username,
                EncryptedConnectionString = EncryptConnectionString(connectionString),
                TrustCertificate = request.TrustCertificate,
                Timeout = request.Timeout > 0 ? request.Timeout : 30,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                LastSuccessfulConnection = DateTime.UtcNow,
                Status = ConnectionStatus.Connected
            };

            dbContext.ServerConnections.Add(connection);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added connection {ConnectionId}", connection.Id);

            try { await _prometheusExporter.ExportTargetsAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to update Prometheus targets"); }

            return (true, "Connection added successfully.", connection);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(bool Success, string Message)> RemoveConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var connection = await dbContext.ServerConnections.FindAsync([connectionId], cancellationToken);
            if (connection == null) return (false, "Connection not found.");

            dbContext.ServerConnections.Remove(connection);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Removed connection {ConnectionId}", connectionId);

            try { await _prometheusExporter.ExportTargetsAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to update Prometheus targets"); }

            return (true, "Connection removed successfully.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(bool Success, string Message)> SetConnectionEnabledAsync(
        string connectionId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var connection = await dbContext.ServerConnections.FindAsync([connectionId], cancellationToken);
            if (connection == null) return (false, "Connection not found.");

            connection.IsEnabled = enabled;
            await dbContext.SaveChangesAsync(cancellationToken);

            try { await _prometheusExporter.ExportTargetsAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to update Prometheus targets"); }

            return (true, $"Connection {(enabled ? "enabled" : "disabled")}.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public ServerConnection? GetConnection(string connectionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        return dbContext.ServerConnections.AsNoTracking().FirstOrDefault(c => c.Id == connectionId);
    }

    public string? GetConnectionString(string connectionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var connection = dbContext.ServerConnections.AsNoTracking().FirstOrDefault(c => c.Id == connectionId);

        if (connection?.EncryptedConnectionString == null) return null;

        try { return DecryptConnectionString(connection.EncryptedConnectionString); }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogWarning(ex, "Decryption failed for {Id} - encryption key may have been lost. Connection requires reauthentication.", connectionId);

            MarkConnectionForReauthentication(connectionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encryption error for {Id}", connectionId);
            return null;
        }
    }

    private void MarkConnectionForReauthentication(string connectionId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            var connection = dbContext.ServerConnections.Find(connectionId);
            if (connection != null)
            {
                connection.Status = ConnectionStatus.RequiresReauthentication;
                connection.LastError = "Encryption key lost. Please re-enter connection credentials.";
                connection.RequiresReauthenticationSince ??= DateTime.UtcNow;
                dbContext.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark connection {Id} for reauthentication", connectionId);
        }
    }

    /// <summary>
    /// Updates credentials for an existing connection (e.g., after encryption key loss).
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateConnectionCredentialsAsync(
        string connectionId,
        string? username,
        string? password,
        bool useWindowsAuth,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var connection = await dbContext.ServerConnections.FindAsync([connectionId], cancellationToken);
            if (connection == null) return (false, "Connection not found.");

            var request = new AddConnectionRequest
            {
                Server = connection.Server,
                Database = connection.Database,
                UseWindowsAuth = useWindowsAuth,
                Username = username,
                Password = password,
                TrustCertificate = connection.TrustCertificate,
                Timeout = connection.Timeout
            };

            var connectionString = BuildConnectionString(request);
            var testResult = await TestConnectionAsync(connectionString);
            if (!testResult.Success) return (false, $"Connection test failed: {testResult.Message}");

            connection.UseWindowsAuth = useWindowsAuth;
            connection.Username = useWindowsAuth ? null : username;
            connection.EncryptedConnectionString = EncryptConnectionString(connectionString);
            connection.Status = ConnectionStatus.Connected;
            connection.LastError = null;
            connection.LastSuccessfulConnection = DateTime.UtcNow;
            connection.RequiresReauthenticationSince = null;

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated credentials for connection {ConnectionId}", connectionId);

            try { await _prometheusExporter.ExportTargetsAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to update Prometheus targets"); }

            return (true, "Credentials updated successfully.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<ServerConnection> GetEnabledConnections()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        return dbContext.ServerConnections
            .AsNoTracking()
            .Where(c => c.IsEnabled)
            .ToList()
            .AsReadOnly();
    }

    public MultiConnectionStatusResponse GetStatus()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var connections = dbContext.ServerConnections.AsNoTracking().ToList();

        return new MultiConnectionStatusResponse
        {
            MaxConnections = _maxConnections,
            ActiveConnections = connections.Count,
            HealthyConnections = connections.Count(c => c.Status == ConnectionStatus.Connected),
            FailedConnections = connections.Count(c => c.Status == ConnectionStatus.Error || c.Status == ConnectionStatus.Disconnected),
            Connections = connections.Select(MapToResponse).ToList()
        };
    }



    public async Task<(bool Success, string Message, string? ServerVersion)> TestConnectionAsync(string connectionString)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return (true, "Connection successful.", connection.ServerVersion);
        }
        catch (SqlException ex)
        {
            return (false, GetFriendlyErrorMessage(ex), null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> TestStoredConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(connectionId);
        if (string.IsNullOrEmpty(connectionString))
        {
            await UpdateConnectionStatusAsync(connectionId, ConnectionStatus.Error, "Missing connection string.");
            return (false, "Missing connection string.");
        }

        await UpdateConnectionStatusAsync(connectionId, ConnectionStatus.Testing, null);

        var result = await TestConnectionAsync(connectionString);

        if (result.Success)
            await UpdateConnectionStatusAsync(connectionId, ConnectionStatus.Connected, null);
        else
            await UpdateConnectionStatusAsync(connectionId, ConnectionStatus.Error, result.Message);

        return (result.Success, result.Message);
    }

    public async Task TestAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var enabledConnections = GetEnabledConnections();
        foreach (var connection in enabledConnections)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try { await TestStoredConnectionAsync(connection.Id, cancellationToken); }
            catch { /* Logged inside */ }
        }
    }

    /// <summary>
    /// Disconnects a connection - stops data collection by setting status to Disconnected.
    /// </summary>
    public async Task<(bool Success, string Message)> DisconnectConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var connection = await dbContext.ServerConnections.FindAsync([connectionId], cancellationToken);
            if (connection == null) return (false, "Connection not found.");

            connection.Status = ConnectionStatus.Disconnected;
            connection.LastError = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Disconnected connection {ConnectionId}", connectionId);
            return (true, "Connection disconnected. Data collection stopped.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes connections that have been in RequiresReauthentication status for longer than the specified duration.
    /// </summary>
    public async Task<int> CleanupStaleReauthConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        var threshold = maxAge ?? TimeSpan.FromHours(24);
        var cutoffTime = DateTime.UtcNow - threshold;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var staleConnections = await dbContext.ServerConnections
                .Where(c => c.Status == ConnectionStatus.RequiresReauthentication
                         && c.RequiresReauthenticationSince.HasValue
                         && c.RequiresReauthenticationSince.Value < cutoffTime)
                .ToListAsync(cancellationToken);

            if (staleConnections.Count == 0) return 0;

            foreach (var conn in staleConnections)
            {
                _logger.LogInformation(
                    "Removed stale connection {ConnectionId} ({Name}) - required reauthentication since {Since}",
                    conn.Id, conn.Name, conn.RequiresReauthenticationSince);
            }

            dbContext.ServerConnections.RemoveRange(staleConnections);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Cleaned up {Count} stale connections requiring reauthentication", staleConnections.Count);
            return staleConnections.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateConnectionStatusAsync(string connectionId, ConnectionStatus status, string? errorMessage)
    {
        await _lock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var connection = await dbContext.ServerConnections.FindAsync(connectionId);
            if (connection != null)
            {
                connection.Status = status;
                connection.LastError = errorMessage;
                if (status == ConnectionStatus.Connected)
                {
                    connection.LastSuccessfulConnection = DateTime.UtcNow;
                    connection.LastError = null;
                }
                await dbContext.SaveChangesAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }



    private string BuildConnectionString(AddConnectionRequest request)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = request.Server,
            InitialCatalog = request.Database ?? "master",
            IntegratedSecurity = request.UseWindowsAuth,
            TrustServerCertificate = request.TrustCertificate,
            ConnectTimeout = request.Timeout > 0 ? request.Timeout : 30,
            ApplicationName = "PbSqlServerMonitoring",
            Encrypt = SqlConnectionEncryptOption.Optional,
            MaxPoolSize = 2,
            MinPoolSize = 0
        };

        if (!request.UseWindowsAuth)
        {
            builder.UserID = request.Username!;
            builder.Password = request.Password;
        }
        return builder.ConnectionString;
    }

    private string EncryptConnectionString(string connectionString) => _protector.Protect(connectionString);
    private string DecryptConnectionString(string encrypted) => _protector.Unprotect(encrypted);

    private static ConnectionInfoResponse MapToResponse(ServerConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Server = connection.Server,
        Database = connection.Database,
        UseWindowsAuth = connection.UseWindowsAuth,
        Username = connection.Username,
        TrustCertificate = connection.TrustCertificate,
        Timeout = connection.Timeout,
        IsEnabled = connection.IsEnabled,
        CreatedAt = connection.CreatedAt,
        LastSuccessfulConnection = connection.LastSuccessfulConnection,
        Status = connection.Status,
        LastError = connection.LastError
    };

    private static string GetFriendlyErrorMessage(SqlException ex)
    {
        return ex.Number switch
        {
            -2 => "Connection timed out.",
            53 => "Server not found.",
            4060 => "Database unavailable.",
            18456 => "Login failed.",
            _ => $"SQL Error {ex.Number}: {ex.Message}"
        };
    }



    public void Dispose()
    {
        _lock.Dispose();
    }
}
