using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Extensions;

/// <summary>
/// Extension methods for pagination operations.
/// Reduces duplicated pagination logic across controllers.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Converts an enumerable to a paged result with pagination metadata.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    /// <param name="source">The source enumerable</param>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The number of items per page</param>
    /// <returns>A PagedResult containing the items and pagination metadata</returns>
    public static PagedResult<T> ToPagedResult<T>(this IEnumerable<T> source, int page, int pageSize)
    {
        var items = source.ToList();
        var totalCount = items.Count;
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        
        return new PagedResult<T>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0
        };
    }
    
    /// <summary>
    /// Converts an enumerable to a paged result with type projection.
    /// </summary>
    /// <typeparam name="TSource">The source type</typeparam>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="source">The source enumerable</param>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The number of items per page</param>
    /// <param name="selector">The projection function</param>
    /// <returns>A PagedResult containing the projected items and pagination metadata</returns>
    public static PagedResult<TResult> ToPagedResult<TSource, TResult>(
        this IEnumerable<TSource> source, 
        int page, 
        int pageSize, 
        Func<TSource, TResult> selector)
    {
        var items = source.ToList();
        var totalCount = items.Count;
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToList();
        
        return new PagedResult<TResult>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0
        };
    }
    
    /// <summary>
    /// Validates and clamps pagination parameters to safe ranges.
    /// </summary>
    /// <param name="page">The page number to validate</param>
    /// <param name="pageSize">The page size to validate</param>
    /// <param name="maxPageSize">The maximum allowed page size</param>
    /// <returns>Tuple of validated (page, pageSize)</returns>
    public static (int Page, int PageSize) ValidatePagination(
        int page, 
        int pageSize, 
        int maxPageSize = MetricsConstants.MaxPageSize)
    {
        return (
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, maxPageSize)
        );
    }
    
    /// <summary>
    /// Validates and clamps a top N value to safe ranges.
    /// </summary>
    /// <param name="topN">The top N value to validate</param>
    /// <param name="maxN">The maximum allowed value</param>
    /// <returns>Validated top N value</returns>
    public static int ValidateTopN(int topN, int maxN = MetricsConstants.MaxTopN)
    {
        return Math.Clamp(topN, 1, maxN);
    }
    
    /// <summary>
    /// Validates and clamps a time range in seconds.
    /// </summary>
    /// <param name="rangeSeconds">The range to validate</param>
    /// <returns>Validated range in seconds</returns>
    public static int ValidateRangeSeconds(int rangeSeconds)
    {
        return Math.Clamp(rangeSeconds, MetricsConstants.MinRangeSeconds, MetricsConstants.MaxRangeSeconds);
    }
}
