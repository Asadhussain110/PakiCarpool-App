using System.ComponentModel.DataAnnotations;
using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.DTOs.Driver;

public class RegisterCarDto : IValidatableObject
{
    [Required]
    [StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(2000, 2030)]
    public int Year { get; set; }

    [Required]
    [StringLength(20)]
    public string LicensePlate { get; set; } = string.Empty;

    [Required]
    [Range(1, 6)]
    public int AvailableSeats { get; set; }

    /// <summary>
    /// Paths returned by POST /api/v1/driver/car/documents with documentType=carPhoto.
    /// Base64 image data is not accepted here.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> PhotoPaths { get; set; } = new();

    /// <summary>
    /// Path returned by POST /api/v1/driver/car/documents with documentType=registration.
    /// Private registration keys are opaque and are not public URLs.
    /// </summary>
    [Required]
    public string RegDocPath { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PhotoPaths is null || PhotoPaths.Any(path => !CarDocumentPath.IsUploadedUrl(path)))
        {
            yield return new ValidationResult(
                "Upload each car photo and use the returned URL.",
                [nameof(PhotoPaths)]);
        }

        if (!CarDocumentPath.IsRegistrationPath(RegDocPath))
        {
            yield return new ValidationResult(
                "Upload the registration image and use the returned document path.",
                [nameof(RegDocPath)]);
        }
    }
}

public class UpdateCarDto : IValidatableObject
{
    [StringLength(50)]
    public string? Make { get; set; }

    [StringLength(50)]
    public string? Model { get; set; }

    [Range(2000, 2030)]
    public int? Year { get; set; }

    [StringLength(20)]
    public string? LicensePlate { get; set; }

    [Range(1, 6)]
    public int? AvailableSeats { get; set; }

    /// <summary>
    /// Previously uploaded car-photo URLs. New image files must first be sent to
    /// POST /api/v1/driver/car/documents.
    /// </summary>
    public List<string>? PhotoPaths { get; set; }

    /// <summary>
    /// Previously uploaded registration-document path; new images must use the upload endpoint.
    /// </summary>
    public string? RegDocPath { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PhotoPaths is not null && PhotoPaths.Any(path => !CarDocumentPath.IsUploadedUrl(path)))
        {
            yield return new ValidationResult(
                "Upload each car photo and use the returned URL.",
                [nameof(PhotoPaths)]);
        }

        if (RegDocPath is not null && !CarDocumentPath.IsRegistrationPath(RegDocPath))
        {
            yield return new ValidationResult(
                "Upload the registration image and use the returned document path.",
                [nameof(RegDocPath)]);
        }
    }
}

public sealed class CarDocumentUploadResponseDto
{
    public string Path { get; set; } = string.Empty;
}

internal static class CarDocumentPath
{
    public static bool IsUploadedUrl(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length <= 2048
        && Uri.TryCreate(path, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http";

    public static bool IsRegistrationPath(string? path)
    {
        const string prefix = "private://driver-registration-documents/";
        if (path is null || path.Length > 256 || !path.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var segments = path[prefix.Length..].Split('/');
        return segments.Length == 2
            && Guid.TryParse(segments[0], out _)
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(segments[1]), "N", out _)
            && Path.GetExtension(segments[1]) is ".jpg" or ".png";
    }
}

public class PostRideDto
{
    [Required]
    [MinLength(2)]
    public List<RideStopDto> Stops { get; set; } = new();

    [Required]
    [Range(0.01, 100000)]
    public decimal Fare { get; set; }

    [Required]
    [Range(1, 6)]
    public int MaxSeats { get; set; }
}

public class RideStopDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(-90, 90)]
    public double Lat { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Lng { get; set; }

    [Required]
    [Range(1, 100)]
    public int SequenceOrder { get; set; }
}

public class LivenessResultDto
{
    [Required]
    [StringLength(7_000_000)]
    public string SelfieBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Nonce that was issued by GET /driver/liveness/challenge.
    /// Must match a valid, unexpired, unused <see cref="PakiCarpool.Domain.Models.LivenessChallenge"/>.
    /// </summary>
    [Required]
    [StringLength(128)]
    public string Nonce { get; set; } = string.Empty;

    public List<LivenessPromptResultDto> PromptResults { get; set; } = new();
}

public class LivenessPromptResultDto
{
    [Required]
    [StringLength(20)]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Raw face measurements captured for this server-selected prompt.
    /// The server evaluates these observations; client pass/fail verdicts are ignored.
    /// </summary>
    public List<LivenessFrameDto> Frames { get; set; } = new();
}

public class LivenessFrameDto
{
    [Range(0d, 1d)]
    public double? LeftEyeOpenProbability { get; set; }

    [Range(0d, 1d)]
    public double? RightEyeOpenProbability { get; set; }

    [Range(-180d, 180d)]
    public double? HeadEulerAngleY { get; set; }

    [Range(0d, 1d)]
    public double? SmilingProbability { get; set; }
}

/// <summary>
/// Response body returned by GET /api/v1/driver/liveness/challenge.
/// The client must embed <see cref="Nonce"/> in the subsequent liveness submission.
/// </summary>
public class LivenessChallengeDto
{
    /// <summary>Base64Url-encoded 32-byte cryptographically random nonce.</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>UTC timestamp after which this nonce is no longer valid.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Ordered prompts selected and persisted by the server.</summary>
    public IReadOnlyList<string> Prompts { get; set; } = [];
}

public class CancelRideDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public class DriverLocationDto
{
    [Required]
    public Guid RideId { get; set; }

    [Required]
    [Range(-90, 90)]
    public double Lat { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Lng { get; set; }
}

public class RideResponseDto
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public decimal DriverRating { get; set; }
    public string? DriverPhotoUrl { get; set; }
    public string CarMake { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;
    public decimal Fare { get; set; }
    public int MaxSeats { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<RideStopResponseDto> Stops { get; set; } = new();
}

public class RideStopResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int SequenceOrder { get; set; }
    public string Status { get; set; } = RideStopStatus.Upcoming.ToString();
    public DateTime? ArrivedAt { get; set; }
    public int? EtaSeconds { get; set; }
}

public class CarStatusResponseDto
{
    public Guid Id { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class BookingDetailDto
{
    public Guid Id { get; set; }
    public Guid PassengerId { get; set; }
    public string PassengerName { get; set; } = string.Empty;
    public string PickupStopName { get; set; } = string.Empty;
    public string DropoffStopName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool IsCashPaid { get; set; }
}

public class LivenessSubmissionResponseDto
{
    public bool Success { get; set; }
    public bool PendingReview { get; set; }
    public string Status { get; set; } = "NotSubmitted";
    public int RemainingAttempts { get; set; }
    public DateTime? BannedUntil { get; set; }
    public string? AccessToken { get; set; }
    public DateTime? SessionIssuedAt { get; set; }
    public string? Message { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? RawRefreshToken { get; set; }
}
