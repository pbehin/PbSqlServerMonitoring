using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Manages user-specific preferences stored in the database.
/// Uses a write-through cache pattern (Memory + DB) for performance.
/// </summary>
public sealed class UserPreferencesService : IDisposable
{
    private readonly ILogger<UserPreferencesService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    
    // In-memory cache: UserIdentifier -> UserPreferences
    private readonly Dictionary<string, UserPreferences> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    
    public UserPreferencesService(
        ILogger<UserPreferencesService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }
    
    /// <summary>
    /// Gets the active connection ID for a user.
    /// </summary>
    public string? GetActiveConnectionId(string userIdentifier)
    {
        // 1. Try Cache (Fast path)
        _lock.Wait();
        try
        {
            if (_cache.TryGetValue(userIdentifier, out var cachedPrefs))
            {
                return cachedPrefs.ActiveConnectionId;
            }
        }
        finally
        {
            _lock.Release();
        }

        // 2. Try Database (Slow path)
        try 
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            
            // Ensure DB is created (in case this is the first run ever)
            // Note: In production relying on EnsureCreated in service path is risky, ideally use Migrations
            // But for this project scope it might be fine, OR we assume EnsureDatabaseAsync called in MetricsService covers it.
            // Let's assume the DB exists.
            
            var entity = dbContext.UserPreferences.Find(userIdentifier);
            if (entity != null)
            {
                var prefs = JsonSerializer.Deserialize<UserPreferences>(entity.PreferencesJson);
                if (prefs != null)
                {
                    // Update cache
                    _lock.Wait();
                    try 
                    {
                        _cache[userIdentifier] = prefs;
                    }
                    finally 
                    { 
                        _lock.Release(); 
                    }
                    return prefs.ActiveConnectionId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user preferences from DB for {UserId}", userIdentifier[..Math.Min(8, userIdentifier.Length)]);
        }

        return null;
    }
    
    /// <summary>
    /// Sets the active connection ID for a user.
    /// </summary>
    public void SetActiveConnectionId(string userIdentifier, string? connectionId)
    {
        UserPreferences prefs;

        // 1. Update Cache
        _lock.Wait();
        try
        {
            if (!_cache.TryGetValue(userIdentifier, out var cachedPrefs) || cachedPrefs is null)
            {
                prefs = new UserPreferences();
                _cache[userIdentifier] = prefs;
            }
            else
            {
                prefs = cachedPrefs;
            }
            
            prefs.ActiveConnectionId = connectionId;
            prefs.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
        
        // 2. Persist to DB (Fire and forget style - or wait? Controller waits, so we should wait)
        // Since we are not async in this signature, we block. Ideally we should be Async.
        // For now, doing it synchronously.
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            
            var entity = dbContext.UserPreferences.Find(userIdentifier);
            var json = JsonSerializer.Serialize(prefs);
            
            if (entity == null)
            {
                entity = new UserPreferenceEntity
                {
                    UserIdentifier = userIdentifier,
                    PreferencesJson = json,
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.UserPreferences.Add(entity);
            }
            else
            {
                entity.PreferencesJson = json;
                entity.LastUpdated = DateTime.UtcNow;
            }
            
            dbContext.SaveChanges();
            
            _logger.LogDebug("Saved preferences for user {UserId}", userIdentifier[..Math.Min(8, userIdentifier.Length)]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist user preferences to DB");
        }
    }
    
    /// <summary>
    /// Gets all preferences for a user.
    /// </summary>
    public UserPreferences? GetUserPreferences(string userIdentifier)
    {
        // Trigger generic load logic (populate cache)
        GetActiveConnectionId(userIdentifier); 
        
        _lock.Wait();
        try
        {
            return _cache.TryGetValue(userIdentifier, out var prefs) ? prefs : null;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Cleans up stale user preferences from CACHE only.
    /// DB cleanup would require a separate maintenance job.
    /// </summary>
    public void CleanupStalePreferences(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        
        _lock.Wait();
        try
        {
            var staleKeys = _cache
                .Where(kvp => kvp.Value.LastUpdated < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in staleKeys)
            {
                _cache.Remove(key);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Disposes the semaphore.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}

/// <summary>
/// User preferences model.
/// </summary>
public class UserPreferences
{
    public string? ActiveConnectionId { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
