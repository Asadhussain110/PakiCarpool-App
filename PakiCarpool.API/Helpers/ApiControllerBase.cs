using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.API.Helpers;

/// <summary>
/// Shared controller helpers: user-id extraction and ApiResponse → IActionResult mapping.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private static readonly HashSet<string> KnownErrorCodes = typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Reads the authenticated user's Guid from JWT claims (userId or NameIdentifier/sub).
    /// </summary>
    protected Guid GetUserId()
    {
        var raw = User.FindFirstValue("userId")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var userId))
            throw new UnauthorizedAccessException("Authenticated user id is missing from the token.");

        return userId;
    }

    /// <summary>
    /// Maps a service <see cref="ApiResponse{T}"/> to the correct HTTP status and body shape.
    /// Success → { data, message }. Failure → { statusCode, error, message }.
    /// </summary>
    protected IActionResult FromResponse<T>(ApiResponse<T> response)
    {
        if (response.IsSuccess)
            return Ok(new { data = response.Data, message = response.Message });

        var (statusCode, errorCode, message) = MapError(response.Message);
        return StatusCode(statusCode, ErrorResponse.Create(statusCode, errorCode, message));
    }

    private static (int StatusCode, string ErrorCode, string Message) MapError(string? rawMessage)
    {
        var message = string.IsNullOrWhiteSpace(rawMessage) ? "An error occurred." : rawMessage.Trim();
        var codePart = message;
        var detail = message;

        var colonIndex = message.IndexOf(':');
        if (colonIndex > 0)
        {
            codePart = message[..colonIndex];
            detail = message[(colonIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(detail))
                detail = message;
        }

        if (!KnownErrorCodes.Contains(codePart))
        {
            return (StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, message);
        }

        var statusCode = codePart switch
        {
            ErrorCodes.Unauthorized or ErrorCodes.TokenExpired => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.BookingTimeout or ErrorCodes.LivenessFailed or ErrorCodes.AccountLocked
                or ErrorCodes.AccountSuspended or ErrorCodes.LivenessBanned => StatusCodes.Status409Conflict,
            ErrorCodes.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
            ErrorCodes.InternalError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };

        var humanMessage = detail == codePart ? Humanize(codePart) : detail;
        return (statusCode, codePart, humanMessage);
    }

    private static string Humanize(string errorCode) => errorCode switch
    {
        ErrorCodes.ValidationError => "Request validation failed.",
        ErrorCodes.StopAlreadyPassed => "The selected stop has already been passed.",
        ErrorCodes.StopSegmentFull => "No seats available for the selected stop segment.",
        ErrorCodes.ActiveBookingExists => "You already have an active booking.",
        ErrorCodes.ActiveRideExists => "You already have an active ride.",
        ErrorCodes.CarNotApproved => "Your car is not approved yet.",
        ErrorCodes.LivenessRequired => "Liveness verification is required.",
        ErrorCodes.LivenessFailed => "Liveness verification failed.",
        ErrorCodes.LivenessBanned => "Liveness verification is temporarily banned.",
        ErrorCodes.Unauthorized => "Authentication is required.",
        ErrorCodes.TokenExpired => "Access token has expired.",
        ErrorCodes.Forbidden => "You do not have permission to perform this action.",
        ErrorCodes.NotFound => "The requested resource was not found.",
        ErrorCodes.BookingTimeout => "The booking request has timed out.",
        ErrorCodes.AccountLocked => "Account is temporarily locked.",
        ErrorCodes.AccountSuspended => "Account is suspended.",
        ErrorCodes.RateLimitExceeded => "Too many requests. Please slow down.",
        ErrorCodes.InternalError => "An unexpected server error occurred.",
        _ => errorCode
    };
}
