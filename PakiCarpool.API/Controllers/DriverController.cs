using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
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

    public DriverController(IDriverService driverService)
    {
        _driverService = driverService;
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
    [ProducesResponseType(typeof(ApiResponse<List<BookingDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRideBookings(Guid rideId)
        => FromResponse(await _driverService.GetRideBookingsAsync(GetUserId(), rideId));

    /// <summary>PUT /driver/bookings/{bookingId}/accept — Accept booking.</summary>
    [HttpPut("bookings/{bookingId:guid}/accept")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptBooking(Guid bookingId)
        => FromResponse(await _driverService.AcceptBookingAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/reject — Reject booking.</summary>
    [HttpPut("bookings/{bookingId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectBooking(Guid bookingId)
        => FromResponse(await _driverService.RejectBookingAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/no-show — Mark passenger no-show.</summary>
    [HttpPut("bookings/{bookingId:guid}/no-show")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkPassengerNoShow(Guid bookingId)
        => FromResponse(await _driverService.MarkPassengerNoShowAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/end-ride — End ride for passenger.</summary>
    [HttpPut("bookings/{bookingId:guid}/end-ride")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndRide(Guid bookingId)
        => FromResponse(await _driverService.EndRideForPassengerAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/bookings/{bookingId}/cash-received — Confirm cash.</summary>
    [HttpPut("bookings/{bookingId:guid}/cash-received")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CashReceived(Guid bookingId)
        => FromResponse(await _driverService.ConfirmCashReceivedAsync(GetUserId(), bookingId));

    /// <summary>PUT /driver/stops/{stopId}/arrive — Mark stop arrived.</summary>
    [HttpPut("stops/{stopId:guid}/arrive")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArriveAtStop(Guid stopId)
        => FromResponse(await _driverService.MarkStopArrivedAsync(GetUserId(), stopId));

    /// <summary>POST /driver/location — Update GPS location.</summary>
    [HttpPost("location")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocation([FromBody] DriverLocationDto dto)
        => FromResponse(await _driverService.UpdateLocationAsync(GetUserId(), dto));

    /// <summary>PUT /driver/bookings/{bookingId}/iam-here — Send arrival notification to passenger.</summary>
    [HttpPut("bookings/{bookingId:guid}/iam-here")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> IAmHere(Guid bookingId)
        => FromResponse(await _driverService.NotifyIAmHereAsync(GetUserId(), bookingId));
}
