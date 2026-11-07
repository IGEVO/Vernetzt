namespace Igevo.Vernetzt.Core.DTOs;

/// <summary>
/// Standard API response wrapper for successful responses.
/// </summary>
/// <typeparam name="T">Type of data being returned.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates if the request was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// The response data.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Optional message for additional context.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Error information (only for failed responses).
    /// </summary>
    public ErrorDetails? Error { get; set; }

    /// <summary>
    /// Timestamp of the response (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Create a successful response with data.
    /// </summary>
    public static ApiResponse<T> CreateSuccess(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    /// <summary>
    /// Create an error response.
    /// </summary>
    public static ApiResponse<T> CreateError(string message, string code = "ERROR", List<string>? details = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = new ErrorDetails
            {
                Code = code,
                Message = message,
                Details = details
            }
        };
    }
}

/// <summary>
/// API response for error cases.
/// </summary>
public class ApiErrorResponse
{
    /// <summary>
    /// Indicates if the request was successful (always false for errors).
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Error information.
    /// </summary>
    public ErrorDetails Error { get; set; } = new();

    /// <summary>
    /// Timestamp of the response (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed error information.
/// </summary>
public class ErrorDetails
{
    /// <summary>
    /// Error code (e.g., "VALIDATION_ERROR", "NOT_FOUND").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional error details or validation errors.
    /// </summary>
    public List<string>? Details { get; set; }
}
