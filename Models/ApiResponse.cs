namespace PbSqlServerMonitoring.Models;

/// <summary>
/// Standardized API response wrapper for consistent error/success responses.
/// Use this for all API endpoints to ensure uniform response format.
/// </summary>
/// <typeparam name="T">The type of the data payload</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// The data payload (null on error).
    /// </summary>
    public T? Data { get; init; }
    
    /// <summary>
    /// Error code for programmatic error handling (optional).
    /// </summary>
    public string? ErrorCode { get; init; }
    
    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };
    
    /// <summary>
    /// Creates a successful response without data.
    /// </summary>
    public static ApiResponse<T> Ok(string message) => new()
    {
        Success = true,
        Message = message
    };
    
    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static ApiResponse<T> Error(string message, string? errorCode = null) => new()
    {
        Success = false,
        Message = message,
        ErrorCode = errorCode
    };
}

/// <summary>
/// Non-generic API response for endpoints that don't return data.
/// </summary>
public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };
    
    public static ApiResponse Error(string message, string? errorCode = null) => new()
    {
        Success = false,
        Message = message,
        ErrorCode = errorCode
    };
}
