namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Interface for managing user-specific preferences.
/// </summary>
public interface IUserPreferencesService : IDisposable
{
    /// <summary>
    /// Gets the active connection ID for a user.
    /// </summary>
    string? GetActiveConnectionId(string userIdentifier);

    /// <summary>
    /// Sets the active connection ID for a user.
    /// </summary>
    void SetActiveConnectionId(string userIdentifier, string? connectionId);

    /// <summary>
    /// Gets all preferences for a user.
    /// </summary>
    UserPreferences? GetUserPreferences(string userIdentifier);

    /// <summary>
    /// Cleans up stale user preferences from cache.
    /// </summary>
    void CleanupStalePreferences(TimeSpan maxAge);
}
