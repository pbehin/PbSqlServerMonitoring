namespace PbSqlServerMonitoring.Configuration;

/// <summary>
/// Centralized constants for metrics and API configuration.
/// Eliminates magic numbers throughout the codebase.
/// </summary>
public static class MetricsConstants
{
    #region Pagination Constants
    
    /// <summary>Default page size for paginated results</summary>
    public const int DefaultPageSize = 100;
    
    /// <summary>Maximum page size for paginated results</summary>
    public const int MaxPageSize = 1000;
    
    /// <summary>Default items per page for smaller result sets</summary>
    public const int DefaultSmallPageSize = 25;
    
    /// <summary>Maximum items per page for smaller result sets</summary>
    public const int MaxSmallPageSize = 100;
    
    #endregion
    
    #region Time Range Constants
    
    /// <summary>Maximum time range in seconds (2 days)</summary>
    public const int MaxRangeSeconds = 172800;
    
    /// <summary>Minimum time range in seconds</summary>
    public const int MinRangeSeconds = 3;
    
    /// <summary>Default time range in seconds (1 minute)</summary>
    public const int DefaultRangeSeconds = 60;
    
    /// <summary>Default query history hours</summary>
    public const double DefaultQueryHistoryHours = 24;
    
    #endregion
    
    #region Query Constants
    
    /// <summary>Default number of top results to return</summary>
    public const int DefaultTopN = 25;
    
    /// <summary>Maximum number of results to return</summary>
    public const int MaxTopN = 100;
    
    /// <summary>Maximum number of characters for query text preview</summary>
    public const int MaxQueryTextLength = 500;
    
    /// <summary>Maximum number of characters for query text in memory</summary>
    public const int MaxQueryTextPreviewLength = 100;
    
    #endregion
    
    #region Timeout Constants
    
    /// <summary>Default query timeout in seconds</summary>
    public const int DefaultQueryTimeoutSeconds = 10;
    
    /// <summary>Extended query timeout for heavy reports</summary>
    public const int ExtendedQueryTimeoutSeconds = 30;
    
    /// <summary>Minimum connection timeout in seconds</summary>
    public const int MinConnectionTimeoutSeconds = 5;
    
    /// <summary>Maximum connection timeout in seconds</summary>
    public const int MaxConnectionTimeoutSeconds = 120;
    
    /// <summary>Default connection timeout in seconds</summary>
    public const int DefaultConnectionTimeoutSeconds = 30;
    
    #endregion
    
    #region Collection Constants
    
    /// <summary>Sample interval for metrics collection in seconds</summary>
    public const int SampleIntervalSeconds = 3;
    
    /// <summary>Number of ticks between persistence saves</summary>
    public const int TicksBetweenSaves = 20;
    
    /// <summary>Memory retention period in minutes</summary>
    public const int MemoryRetentionMinutes = 15;
    
    /// <summary>Maximum pending save queue size</summary>
    public const int MaxPendingSaveQueue = 5000;
    
    #endregion
    
    #region Retention Constants
    
    /// <summary>Default data retention in days</summary>
    public const int DefaultRetentionDays = 7;
    
    /// <summary>Maximum retention in days</summary>
    public const int MaxRetentionDays = 365;
    
    /// <summary>Minimum retention in days</summary>
    public const int MinRetentionDays = 1;
    
    /// <summary>Default cleanup interval in minutes</summary>
    public const int DefaultCleanupIntervalMinutes = 60;
    
    /// <summary>Batch size for cleanup operations</summary>
    public const int CleanupBatchSize = 1000;
    
    #endregion
    
    #region Cache Constants
    
    /// <summary>Default cache duration in minutes for expensive queries</summary>
    public const int DefaultCacheDurationMinutes = 5;
    
    /// <summary>Cache key prefix for missing indexes</summary>
    public const string MissingIndexesCacheKey = "MissingIndexes";
    
    #endregion
    
    #region Rate Limiting Constants
    
    /// <summary>Maximum requests per minute per IP</summary>
    public const int RateLimitPerMinute = 100;
    
    /// <summary>Token bucket replenish period in seconds</summary>
    public const int RateLimitWindowSeconds = 60;
    
    #endregion
}
