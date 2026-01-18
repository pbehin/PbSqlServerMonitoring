using System.Text.RegularExpressions;
using PbSqlServerMonitoring.Configuration;

namespace PbSqlServerMonitoring.Extensions;

/// <summary>
/// Input validation extension methods for API requests.
/// Centralizes validation logic to ensure consistent security checks.
/// </summary>
public static partial class InputValidationExtensions
{
    [GeneratedRegex("^[a-fA-F0-9]{8,16}$", RegexOptions.Compiled)]
    private static partial Regex ConnectionIdRegex();

    /// <summary>
    /// Validates a connection ID from request header.
    /// Returns (isValid, sanitizedId, errorMessage).
    /// </summary>
    public static (bool IsValid, string? SanitizedId, string? Error) ValidateConnectionId(string? connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return (false, null, "Connection ID is required");
        }

        var trimmed = connectionId.Trim();

        if (trimmed.Length < 8 || trimmed.Length > 16)
        {
            return (false, null, "Invalid Connection ID format");
        }

        if (!ConnectionIdRegex().IsMatch(trimmed))
        {
            return (false, null, "Invalid Connection ID format");
        }

        return (true, trimmed.ToLowerInvariant(), null);
    }

    /// <summary>
    /// Validates and sanitizes a server name.
    /// </summary>
    public static (bool IsValid, string? SanitizedServer, string? Error) ValidateServerName(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return (false, null, "Server name is required");
        }

        var trimmed = server.Trim();

        if (trimmed.Length > 256)
        {
            return (false, null, "Server name is too long");
        }

        if (trimmed.Contains(';') || trimmed.Contains('\'') || trimmed.Contains('"'))
        {
            return (false, null, "Invalid characters in server name");
        }

        return (true, trimmed, null);
    }

    /// <summary>
    /// Validates and sanitizes a database name.
    /// </summary>
    public static (bool IsValid, string? SanitizedDatabase, string? Error) ValidateDatabaseName(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return (true, "master", null);
        }

        var trimmed = database.Trim();

        if (trimmed.Length > 128)
        {
            return (false, null, "Database name is too long");
        }

        if (trimmed.Contains(';') || trimmed.Contains('\'') || trimmed.Contains('"') ||
            trimmed.Contains('[') || trimmed.Contains(']'))
        {
            return (false, null, "Invalid characters in database name");
        }

        return (true, trimmed, null);
    }

    /// <summary>
    /// Validates and clamps a timeout value.
    /// </summary>
    public static int ValidateTimeout(int timeout)
    {
        return Math.Clamp(timeout,
            MetricsConstants.MinConnectionTimeoutSeconds,
            MetricsConstants.MaxConnectionTimeoutSeconds);
    }

    /// <summary>
    /// Validates a username for SQL authentication.
    /// </summary>
    public static (bool IsValid, string? SanitizedUsername, string? Error) ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return (false, null, "Username is required for SQL authentication");
        }

        var trimmed = username.Trim();

        if (trimmed.Length > 128)
        {
            return (false, null, "Username is too long");
        }

        if (trimmed.Contains(';') || trimmed.Contains('\'') || trimmed.Contains('"'))
        {
            return (false, null, "Invalid characters in username");
        }

        return (true, trimmed, null);
    }
}
