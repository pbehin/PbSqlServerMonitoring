using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using PbSqlServerMonitoring.Configuration;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Manages SQL Server connection settings securely.
/// 
/// Security Features:
/// - Connection strings are encrypted in memory using Data Protection API
/// - Passwords are never logged
/// - Connection is session-based (cleared on server restart)
/// - TrustServerCertificate defaults to false in production for security
/// </summary>
public sealed class ConnectionService
{
    #region Constants
    
    private const string ProtectionPurpose = "SqlServerMonitoring.ConnectionString";
    
    #endregion

    #region Fields
    
    private readonly IDataProtector _protector;
    private readonly ILogger<ConnectionService> _logger;
    private readonly IWebHostEnvironment _environment;
    private string? _encryptedConnectionString;
    
    #endregion

    #region Constructor
    
    public ConnectionService(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<ConnectionService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
        _logger = logger;
        _environment = environment;

        // Load initial connection string from config (if provided)
        var initialConnectionString = configuration.GetConnectionString("SqlServer");
        if (!string.IsNullOrEmpty(initialConnectionString))
        {
            StoreConnectionString(initialConnectionString);
        }
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets the decrypted connection string.
    /// Returns null if no connection is configured.
    /// </summary>
    public string? GetConnectionString()
    {
        if (string.IsNullOrEmpty(_encryptedConnectionString))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(_encryptedConnectionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt connection string");
            return null;
        }
    }

    /// <summary>
    /// Stores the connection string securely (encrypted in memory).
    /// </summary>
    public void SetConnectionString(string connectionString)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(connectionString);
        
        StoreConnectionString(connectionString);
        _logger.LogInformation("Connection string updated successfully");
    }

    /// <summary>
    /// Clears the stored connection.
    /// </summary>
    public void ClearConnection()
    {
        _encryptedConnectionString = null;
        _logger.LogInformation("Connection cleared");
    }

    /// <summary>
    /// Checks if a connection is configured.
    /// </summary>
    public bool HasConnection => !string.IsNullOrEmpty(_encryptedConnectionString);

    /// <summary>
    /// Builds a connection string from individual parameters.
    /// Validates all inputs before building.
    /// 
    /// Security: TrustServerCertificate defaults to false in production
    /// to enforce SSL certificate validation.
    /// </summary>
    public string BuildConnectionString(ConnectionParameters parameters)
    {
        ValidateParameters(parameters);

        // Determine if we should trust server certificate
        // In production, default to false for security unless explicitly set
        var trustCert = parameters.TrustCertificate;
        if (!trustCert.HasValue)
        {
            trustCert = _environment.IsDevelopment();
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = parameters.Server.Trim(),
            InitialCatalog = parameters.Database?.Trim() ?? "master",
            ConnectTimeout = ClampTimeout(parameters.Timeout),
            TrustServerCertificate = trustCert.Value,
            ApplicationName = "SQL Server Monitor",
            
            // Security: Encrypt connection by default
            Encrypt = true,
            
            // Connection pooling settings
            MaxPoolSize = 20,
            MinPoolSize = 2
        };

        if (parameters.UseWindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(parameters.Username))
            {
                throw new ArgumentException("Username is required for SQL Authentication", nameof(parameters));
            }
            
            builder.UserID = parameters.Username.Trim();
            builder.Password = parameters.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Tests a connection string without storing it.
    /// Returns success status, message, and server version.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(string connectionString)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(connectionString);
        
        try
        {
            using var connection = new SqlConnection(connectionString);
            
            // Short timeout for testing
            connection.ConnectionString = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 10
            }.ConnectionString;

            await connection.OpenAsync();

            // Get server version
            var version = await GetServerVersionAsync(connection);

            return new ConnectionTestResult
            {
                Success = true,
                Message = "Connection successful",
                ServerVersion = version
            };
        }
        catch (SqlException ex)
        {
            _logger.LogWarning("Connection test failed: {Message}", ex.Message);
            return new ConnectionTestResult
            {
                Success = false,
                Message = GetFriendlyErrorMessage(ex)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed unexpectedly");
            return new ConnectionTestResult
            {
                Success = false,
                Message = "Connection failed: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Returns connection info for display (without sensitive data).
    /// </summary>
    public ConnectionInfo GetConnectionInfo()
    {
        var connectionString = GetConnectionString();
        
        if (string.IsNullOrEmpty(connectionString))
        {
            return new ConnectionInfo { IsConfigured = false };
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return new ConnectionInfo
            {
                IsConfigured = true,
                Server = builder.DataSource,
                Database = builder.InitialCatalog,
                UseWindowsAuth = builder.IntegratedSecurity,
                Username = builder.IntegratedSecurity ? null : builder.UserID,
                TrustCertificate = builder.TrustServerCertificate,
                Timeout = builder.ConnectTimeout
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse stored connection string");
            return new ConnectionInfo { IsConfigured = false };
        }
    }
    
    #endregion

    #region Private Methods
    
    private void StoreConnectionString(string connectionString)
    {
        _encryptedConnectionString = _protector.Protect(connectionString);
    }
    
    private void ValidateParameters(ConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        
        if (string.IsNullOrWhiteSpace(parameters.Server))
        {
            throw new ArgumentException("Server name is required", nameof(parameters));
        }

        if (!parameters.UseWindowsAuth)
        {
            if (string.IsNullOrWhiteSpace(parameters.Username))
            {
                throw new ArgumentException("Username is required for SQL Authentication", nameof(parameters));
            }
            
            if (string.IsNullOrEmpty(parameters.Password))
            {
                throw new ArgumentException("Password is required for SQL Authentication", nameof(parameters));
            }
        }
    }
    
    private static int ClampTimeout(int timeout)
    {
        return Math.Clamp(timeout, 
            MetricsConstants.MinConnectionTimeoutSeconds, 
            MetricsConstants.MaxConnectionTimeoutSeconds);
    }
    
    private static async Task<string> GetServerVersionAsync(SqlConnection connection)
    {
        using var command = new SqlCommand("SELECT @@VERSION", connection);
        command.CommandTimeout = 5;
        
        var result = await command.ExecuteScalarAsync();
        var fullVersion = result?.ToString() ?? "Unknown";
        
        // Return only first line
        var firstNewline = fullVersion.IndexOf('\n');
        return firstNewline > 0 ? fullVersion[..firstNewline].Trim() : fullVersion;
    }
    
    private static string GetFriendlyErrorMessage(SqlException ex)
    {
        return ex.Number switch
        {
            -2 => "Connection timed out. Check server name and network.",
            18456 => "Login failed. Check username and password.",
            4060 => "Cannot open database. Check database name exists.",
            40615 => "Cannot connect to server. Check firewall rules.",
            -1 => "Cannot connect to server. Verify server name and ensure SQL Server is running.",
            _ => $"SQL Error {ex.Number}: {ex.Message}"
        };
    }
    
    #endregion
}

#region Supporting Types

/// <summary>
/// Parameters for building a connection string.
/// 
/// Security: TrustCertificate is now nullable.
/// - null = use environment-based default (true in dev, false in prod)
/// - true = explicitly trust server certificate
/// - false = require valid certificate
/// </summary>
public sealed record ConnectionParameters
{
    public required string Server { get; init; }
    public string? Database { get; init; } = "master";
    public bool UseWindowsAuth { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    
    /// <summary>
    /// Whether to trust the server certificate.
    /// Null = use environment default (true in dev, false in prod)
    /// </summary>
    public bool? TrustCertificate { get; init; }
    
    public int Timeout { get; init; } = MetricsConstants.DefaultConnectionTimeoutSeconds;
}

/// <summary>
/// Result of a connection test operation.
/// </summary>
public sealed record ConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ServerVersion { get; init; }
}

/// <summary>
/// Connection information for display (no sensitive data).
/// </summary>
public sealed record ConnectionInfo
{
    public bool IsConfigured { get; init; }
    public string? Server { get; init; }
    public string? Database { get; init; }
    public bool UseWindowsAuth { get; init; }
    public string? Username { get; init; }
    public bool TrustCertificate { get; init; }
    public int Timeout { get; init; }
}

#endregion
