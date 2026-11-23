using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Driver endpoints: car, rides, bookings, stops, location.
/// Base route: /api/v1/driver
/// </summary>
[Route("api/v1/driver")]
[Authorize(Policy = RolePolicy.DriverPolicy)]
public sealed class DriverController : ApiControllerBase
{
    private readonly IDriverService _driverService;
    private readonly IStorageService _storageService;

    public DriverController(IDriverService driverService, IStorageService storageService)
    {
        _driverService = driverService;
        _storageService = storageService;
    }

    /// <summary>POST /driver/car/documents — Upload a car photo or registration image.</summary>
    [HttpPost("car/documents")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<CarDocumentUploadResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadCarDocument(
        [FromForm] IFormFile file,
        [FromForm] string documentType,
        CancellationToken cancellationToken)
    {
        const long maxImageSize = 5 * 1024 * 1024;
        if (file is null || file.Length is 0 or > maxImageSize)
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Choose a non-empty image no larger than 5 MB."));

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var bytes = stream.ToArray();
        var isJpeg = bytes.Length >= 3
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isPng = bytes.AsSpan().StartsWith(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var contentType = isJpeg ? "image/jpeg" : isPng ? "image/png" : null;
        if (contentType is null)
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Only valid JPEG and PNG images are supported."));

        string path;
        if (string.Equals(documentType, "registration", StringComparison.OrdinalIgnoreCase))
        {
            path = await _storageService.UploadPrivateCarDocumentAsync(
                GetUserId(),
                bytes,
                contentType,
                cancellationToken);
        }
        else if (string.Equals(documentType, "carPhoto", StringComparison.OrdinalIgnoreCase))
        {
            var extension = contentType == "image/jpeg" ? "jpg" : "png";
            var objectName = $"{GetUserId():D}/{Guid.NewGuid():N}.{extension}";
            path = await _storageService.UploadAsync(
                "driver-cars",
                objectName,
                bytes,
                contentType,
                cancellationToken);
        }
        else
        {
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "documentType must be 'carPhoto' or 'registration'."));
        }

        return Ok(new
        {
            data = new CarDocumentUploadResponseDto { Path = path },
            message = "Image uploaded."
        });
    }

    /// <summary>POST /driver/car — Register car.</summary>
    [HttpPost("car")]
    [ProducesResponseType(typeof(ApiResponse<CarStatusResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterCar([FromBody] RegisterCarDto dto)
        => FromResponse(await _driverService.RegisterCarAsync(GetUserId(), dto));

    /// <summary>PUT /driver/car/{carId} — Update car details.</summary>
    [HttpPut("car/{carId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CarStatusResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCar(Guid carId, [FromBody] UpdateCarDto dto)
        => FromResponse(await _driverService.UpdateCarAsync(GetUserId(), carId, dto));

    /// <summary>GET /driver/car/status — Get car approval status.</summary>
    [HttpGet("car/status")]
    [ProducesResponseType(typeof(ApiResponse<List<CarStatusResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCarStatus()
        => FromResponse(await _driverService.GetCarStatusAsync(GetUserId()));

    /// <summary>POST /driver/rides — Post new ride.</summary>
    [HttpPost("rides")]
    [ProducesResponseType(typeof(ApiResponse<RideResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PostRide([FromBody] PostRideDto dto)
        => FromResponse(await _driverService.PostRideAsync(GetUserId(), dto));

    /// <summary>DELETE /driver/rides/{rideId} — Cancel ride.</summary>
    [HttpDelete("rides/{rideId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelRide(Guid rideId, [FromBody] CancelRideDto dto)
        => FromResponse(await _driverService.CancelRideAsync(GetUserId(), rideId, dto));

    /// <summary>GET /driver/rides/active — Get active ride.</summary>
    [HttpGet("rides/active")]
    [ProducesResponseType(typeof(ApiResponse<RideResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveRide()
        => FromResponse(await _driverService.GetActiveRideAsync(GetUserId()));

    /// <summary>GET /driver/rides/{rideId}/bookings — List bookings on a ride.</summary>
    [HttpGet("rides/{rideId:guid}/bookings")]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<BookingDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRideBookings(
        Guid rideId,
        [FromQuery] PaginationParams pagination)
    {
        if (!Request.Query.ContainsKey(nameof(PaginationParams.PageSize)))
            pagination.PageSize = 50;

        return FromResponse(await _driverService.GetRideBookingsAsync(
            GetUserId(),
            rideId,
            pagination));
    }

    /// <summary>PUT /driver/bookings/{bookingId}/accept — Accept booking.</summary>
    [HttpPost("bookings/{bookingId:guid}/accept")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptBooking(Guid bookingId)
        => FromResponse(await _driverService.AcceptBookingAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/reject — Reject booking.</summary>
    [HttpPost("bookings/{bookingId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectBooking(Guid bookingId)
        => FromResponse(await _driverService.RejectBookingAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/no-show — Mark passenger no-show.</summary>
    [HttpPost("bookings/{bookingId:guid}/no-show")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkPassengerNoShow(Guid bookingId, CancellationToken cancellationToken)
        => FromResponse(await _driverService.MarkPassengerNoShowAsync(GetUserId(), bookingId, cancellationToken));

    /// <summary>PUT /driver/bookings/{bookingId}/end-ride — End ride for passenger.</summary>
    [HttpPost("bookings/{bookingId:guid}/end-ride")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndRide(Guid bookingId)
        => FromResponse(await _driverService.EndRideForPassengerAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/cash-received — Confirm cash.</summary>
    [HttpPost("bookings/{bookingId:guid}/cash-received")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CashReceived(Guid bookingId)
        => FromResponse(await _driverService.ConfirmCashReceivedAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/stops/{stopId}/begin-travel — Begin travelling to the next stop.</summary>
    [HttpPost("stops/{stopId:guid}/begin-travel")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginStopTravel(Guid stopId)
        => FromResponse(await _driverService.BeginStopTravelAsync(GetUserId(), stopId));

    /// <summary>PUT /driver/stops/{stopId}/arrive — Mark stop arrived.</summary>
    [HttpPost("stops/{stopId:guid}/arrive")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArriveAtStop(Guid stopId)
        => FromResponse(await _driverService.MarkStopArrivedAsync(GetUserId(), stopId));

    /// <summary>POST /driver/location — Update GPS location.</summary>
    [HttpPost("location")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocation([FromBody] DriverLocationDto dto)
        => FromResponse(await _driverService.UpdateLocationAsync(GetUserId(), dto));

    /// <summary>PUT /driver/bookings/{bookingId}/driver-arrived — Send arrival notification to passenger.</summary>
    [HttpPost("bookings/{bookingId:guid}/driver-arrived")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> IAmHere(Guid bookingId)
        => FromResponse(await _driverService.NotifyIAmHereAsync(GetUserId(), bookingId));
}
