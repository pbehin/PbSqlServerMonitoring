namespace PbSqlServerMonitoring.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Configuration for multiple SQL Server connections.
/// </summary>
public sealed class MultiConnectionConfig
{
    /// <summary>
    /// Maximum number of SQL Server connections allowed.
    /// Default is 5, configurable via appsettings or environment variable.
    /// </summary>
    public int MaxConnections { get; set; } = 5;

    /// <summary>
    /// List of configured connections.
    /// </summary>
    public List<ServerConnection> Connections { get; set; } = new();
}

/// <summary>
/// Represents a single SQL Server connection configuration.
/// Stored in database with encrypted connection string.
/// </summary>
public sealed class ServerConnection
{
    /// <summary>
    /// Unique identifier for this connection.
    /// </summary>
    [Key]
    [MaxLength(16)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>
    /// User ID that owns this connection (for multi-tenant isolation).
    /// </summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>
    /// Display name for the connection (user-friendly identifier).
    /// </summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SQL Server hostname or IP address.
    /// </summary>
    [MaxLength(500)]
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Database name to connect to.
    /// </summary>
    [MaxLength(128)]
    public string Database { get; set; } = "master";

    /// <summary>
    /// Whether to use Windows Authentication.
    /// </summary>
    public bool UseWindowsAuth { get; set; }

    /// <summary>
    /// SQL Server username (only used if not using Windows Auth).
    /// </summary>
    [MaxLength(128)]
    public string? Username { get; set; }

    /// <summary>
    /// Encrypted connection string stored in the database.
    /// </summary>
    public string? EncryptedConnectionString { get; set; }

    /// <summary>
    /// Whether to trust the server certificate.
    /// </summary>
    public bool TrustCertificate { get; set; } = true;

    /// <summary>
    /// Connection encryption mode: disable, false, true, strict.
    /// </summary>
    [MaxLength(20)]
    public string Encrypt { get; set; } = "true";

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Whether this connection is enabled for monitoring.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When this connection was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time this connection was successfully tested.
    /// </summary>
    public DateTime? LastSuccessfulConnection { get; set; }

    /// <summary>
    /// Current connection status.
    /// </summary>
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Unknown;

    /// <summary>
    /// Last error message if connection failed.
    /// </summary>
    [MaxLength(2000)]
    public string? LastError { get; set; }

    /// <summary>
    /// When the connection entered RequiresReauthentication status.
    /// Used for automatic cleanup of stale connections.
    /// </summary>
    public DateTime? RequiresReauthenticationSince { get; set; }
}

/// <summary>
/// Status of a server connection.
/// </summary>
public enum ConnectionStatus
{
    Unknown = 0,
    Connected = 1,
    Disconnected = 2,
    Error = 3,
    Testing = 4,
    /// <summary>
    /// Connection credentials cannot be decrypted (encryption key lost).
    /// User must re-enter credentials to restore the connection.
    /// </summary>
    RequiresReauthentication = 5
}

/// <summary>
/// Request DTO for adding/updating a connection.
/// </summary>
public sealed class AddConnectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = "master";
    public bool UseWindowsAuth { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool TrustCertificate { get; set; } = true;
    /// <summary>
    /// Connection encryption mode: disable, false, true, strict.
    /// </summary>
    public string Encrypt { get; set; } = "true";
    public int Timeout { get; set; } = 30;
}

/// <summary>
/// Response DTO for connection info (without sensitive data).
/// </summary>
public sealed class ConnectionInfoResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public bool UseWindowsAuth { get; set; }
    public string? Username { get; set; }
    public bool TrustCertificate { get; set; }
    public string Encrypt { get; set; } = "true";
    public int Timeout { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSuccessfulConnection { get; set; }
    public ConnectionStatus Status { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Response for multi-connection status overview.
/// </summary>
public sealed class MultiConnectionStatusResponse
{
    public int MaxConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int HealthyConnections { get; set; }
    public int FailedConnections { get; set; }
    public List<ConnectionInfoResponse> Connections { get; set; } = new();
}
