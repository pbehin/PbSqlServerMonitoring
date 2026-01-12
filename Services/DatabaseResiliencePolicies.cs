using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Provides resilience policies for database operations.
/// Uses Polly for retry policies with exponential backoff.
/// 
/// Transient SQL Server errors that can be retried:
/// - -2: Timeout
/// - 1205: Deadlock victim
/// - 49918: Cannot process request (Azure elastic pool)
/// - 49919: Rate limit exceeded (Azure)
/// - 49920: Too busy (Azure)
/// - 4060: Cannot open database (transient in some cases)
/// - 40197: Service encountered error
/// - 40501: Service busy
/// - 40613: Database not currently available
/// </summary>
public static class DatabaseResiliencePolicies
{
    /// <summary>
    /// SQL error codes that are considered transient and can be retried.
    /// </summary>
    private static readonly int[] TransientSqlErrorCodes = 
    {
        -2,     // Timeout
        1205,   // Deadlock victim
        49918,  // Cannot process request (Azure elastic pool)
        49919,  // Rate limit exceeded (Azure)
        49920,  // Service busy (Azure)
        40197,  // Service encountered error
        40501,  // Service busy
        40613,  // Database not currently available
        10053,  // Transport-level error (connection forcibly closed)
        10054,  // Transport-level error (connection reset)
        10060,  // Network timeout
        40143,  // Connection could not be opened
        64      // Named pipe error
    };

    /// <summary>
    /// Creates a retry pipeline for database operations.
    /// Uses exponential backoff with jitter.
    /// </summary>
    /// <param name="logger">Logger for retry events</param>
    /// <param name="maxRetries">Maximum retry attempts (default: 3)</param>
    /// <returns>A configured resilience pipeline</returns>
    public static ResiliencePipeline<T> CreateDbRetryPipeline<T>(
        ILogger? logger = null,
        int maxRetries = 3)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = maxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<SqlException>(ex => IsTransientError(ex))
                    .Handle<TimeoutException>(),
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        "Database operation failed (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms. Error: {Error}",
                        args.AttemptNumber,
                        maxRetries,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "Unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a retry pipeline for void database operations.
    /// </summary>
    public static ResiliencePipeline CreateDbRetryPipeline(
        ILogger? logger = null,
        int maxRetries = 3)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>(ex => IsTransientError(ex))
                    .Handle<TimeoutException>(),
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        "Database operation failed (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms. Error: {Error}",
                        args.AttemptNumber,
                        maxRetries,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "Unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Determines if a SQL exception is transient and can be retried.
    /// </summary>
    public static bool IsTransientError(SqlException ex)
    {
        if (ex == null) return false;

        // Check each error in the exception
        foreach (SqlError error in ex.Errors)
        {
            if (TransientSqlErrorCodes.Contains(error.Number))
            {
                return true;
            }
        }

        return false;
    }
}
