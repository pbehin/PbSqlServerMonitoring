using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Extensions;

/// <summary>
/// Extension methods for pagination operations.
/// Reduces duplicated pagination logic across controllers.
/// Provides both IEnumerable (in-memory) and IQueryable (database) variants.
/// </summary>
public static class PaginationExtensions
{
    #region IEnumerable Pagination (In-Memory)
    
    /// <summary>
    /// Converts an enumerable to a paged result with pagination metadata.
    /// WARNING: This loads all items into memory first. Use IQueryable overload for large datasets.
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
    /// WARNING: This loads all items into memory first. Use IQueryable overload for large datasets.
    /// </summary>
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
    
    #endregion
    
    #region IQueryable Pagination (Database-Level)
    
    /// <summary>
    /// Converts an IQueryable to a paged result, executing Skip/Take at the database level.
    /// This is more efficient than the IEnumerable version for large datasets.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    /// <param name="source">The source queryable</param>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The number of items per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A PagedResult containing the items and pagination metadata</returns>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source, 
        int page, 
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Get total count at database level
        var totalCount = await source.CountAsync(cancellationToken);
        
        // Get only the items for the current page
        var pagedItems = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        
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
    /// Converts an IQueryable to a paged result with type projection at the database level.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source, 
        int page, 
        int pageSize, 
        System.Linq.Expressions.Expression<Func<TSource, TResult>> selector,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await source.CountAsync(cancellationToken);
        
        var pagedItems = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);
        
        return new PagedResult<TResult>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0
        };
    }
    
    #endregion
    
    #region Validation Helpers
    
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
    
    #endregion
}

