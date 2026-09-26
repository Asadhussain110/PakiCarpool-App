using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Passenger endpoints: ride browsing, booking lifecycle, no-show.
/// Base route: /api/v1/passenger
/// </summary>
[Route("api/v1/passenger")]
[Authorize(Policy = RolePolicy.PassengerPolicy)]
public sealed class PassengerController : ApiControllerBase
{
    private readonly IPassengerService _passengerService;

    public PassengerController(IPassengerService passengerService)
    {
        _passengerService = passengerService;
    }

    /// <summary>GET /passenger/rides — List available rides.</summary>
    [HttpGet("rides")]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<RideResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableRides([FromQuery] PaginationParams pagination)
        => FromResponse(await _passengerService.GetAvailableRidesAsync(pagination));

    /// <summary>GET /passenger/rides/{rideId} — Ride detail.</summary>
    [HttpGet("rides/{rideId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RideResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRideDetail(Guid rideId)
        => FromResponse(await _passengerService.GetRideDetailAsync(rideId));

    /// <summary>POST /passenger/bookings — Create booking request.</summary>
    [HttpPost("bookings")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingDto dto)
        => FromResponse(await _passengerService.CreateBookingAsync(GetUserId(), dto));

    /// <summary>DELETE /passenger/bookings/{bookingId} — Cancel booking with reason.</summary>
    [HttpDelete("bookings/{bookingId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelBooking(Guid bookingId, [FromBody] CancelBookingDto dto)
        => FromResponse(await _passengerService.CancelBookingAsync(GetUserId(), bookingId, dto));

    /// <summary>GET /passenger/bookings/active — Get active booking.</summary>
    [HttpGet("bookings/active")]
    [ProducesResponseType(typeof(ApiResponse<ActiveBookingResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveBooking()
        => FromResponse(await _passengerService.GetActiveBookingAsync(GetUserId()));

    /// <summary>PUT /passenger/bookings/{bookingId}/no-show — Mark driver no-show.</summary>
    [HttpPut("bookings/{bookingId:guid}/no-show")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkDriverNoShow(Guid bookingId)
        => FromResponse(await _passengerService.MarkDriverNoShowAsync(GetUserId(), bookingId));
}
