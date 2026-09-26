using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PakiCarpool.Domain.DTOs.Common;

/// <summary>
/// Standard success response wrapper: { data, message }.
/// IsSuccess is internal for controllers and is never serialized to clients.
/// </summary>
public class ApiResponse<T>
{
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the operation succeeded. Not included in the JSON response body.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess { get; set; }

    public static ApiResponse<T> Success(T data, string message = "Success")
    {
        return new ApiResponse<T> { Data = data, Message = message, IsSuccess = true };
    }

    public static ApiResponse<T> SuccessMessage(string message)
    {
        return new ApiResponse<T> { Message = message, IsSuccess = true };
    }
}

/// <summary>
/// Standard error response: { statusCode, error, message }
/// </summary>
public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public static ErrorResponse Create(int statusCode, string error, string message)
    {
        return new ErrorResponse
        {
            StatusCode = statusCode,
            Error = error,
            Message = message
        };
    }
}

public class RatingDto
{
    [Required]
    public Guid RideId { get; set; }

    [Required]
    public Guid RateeId { get; set; }

    [Required]
    [Range(1, 5)]
    public int Stars { get; set; }

    [StringLength(300)]
    public string? ReviewText { get; set; }
}

public class FlagDto
{
    [Required]
    public Guid ReporteeId { get; set; }

    [Required]
    public Guid RideId { get; set; }

    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public class PaginationParams
{
    private const int MaxPageSize = 50;
    private int _pageSize = 10;

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
    }
}

/// <summary>
/// Paginated result wrapper for list endpoints.
/// </summary>
public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public static PaginatedResult<T> Create(List<T> items, int totalCount, int page, int pageSize)
    {
        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}
