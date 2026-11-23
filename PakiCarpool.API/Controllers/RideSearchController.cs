using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;
using PakiCarpool.Services.Options;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Passenger ride-search endpoint.
/// Base route: /api/v1/rides
/// </summary>
[Route("api/v1/rides")]
[Authorize(Policy = RolePolicy.PassengerPolicy)]
public sealed class RideSearchController : ApiControllerBase
{
    private readonly IRideSearchService _rideSearchService;
    private readonly RideSearchOptions _options;

    public RideSearchController(
        IRideSearchService rideSearchService,
        IOptions<RideSearchOptions> options)
    {
        _rideSearchService = rideSearchService;
        _options = options.Value;
    }

    /// <summary>GET /api/v1/rides/search/config — Get configured search radius bounds.</summary>
    [HttpGet("search/config")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult GetSearchConfig()
    {
        var config = new
        {
            defaultRadiusMeters = _options.DefaultRadiusMeters,
            minRadiusMeters = _options.MinRadiusMeters,
            maxRadiusMeters = _options.MaxRadiusMeters
        };

        return FromResponse(ApiResponse<object>.Success(config));
    }

    /// <summary>GET /api/v1/rides/search — Find rides matching the requested route.</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<RideSearchResultDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchRides(
        [FromQuery] double? startLatitude,
        [FromQuery] double? startLongitude,
        [FromQuery] double? endLatitude,
        [FromQuery] double? endLongitude,
        [FromQuery] double? radiusMeters)
    {
        if (!startLatitude.HasValue || !startLongitude.HasValue ||
            !endLatitude.HasValue || !endLongitude.HasValue ||
            !double.IsFinite(startLatitude.Value) || !double.IsFinite(startLongitude.Value) ||
            !double.IsFinite(endLatitude.Value) || !double.IsFinite(endLongitude.Value) ||
            startLatitude is < -90 or > 90 || endLatitude is < -90 or > 90 ||
            startLongitude is < -180 or > 180 || endLongitude is < -180 or > 180)
        {
            return BadRequest(new
            {
                statusCode = StatusCodes.Status400BadRequest,
                error = ErrorCodes.ValidationError,
                message = "Valid start and end coordinates are required."
            });
        }

        var effectiveRadius = radiusMeters ?? _options.DefaultRadiusMeters;
        if (!double.IsFinite(effectiveRadius) ||
            effectiveRadius < _options.MinRadiusMeters ||
            effectiveRadius > _options.MaxRadiusMeters)
        {
            return BadRequest(new
            {
                statusCode = StatusCodes.Status400BadRequest,
                error = ErrorCodes.ValidationError,
                message = $"Radius must be between {_options.MinRadiusMeters} and {_options.MaxRadiusMeters} meters."
            });
        }

        var request = new RideSearchRequestDto
        {
            StartLatitude = startLatitude.Value,
            StartLongitude = startLongitude.Value,
            EndLatitude = endLatitude.Value,
            EndLongitude = endLongitude.Value,
            RadiusMeters = effectiveRadius
        };
        var results = await _rideSearchService.SearchRidesAsync(request);

        return FromResponse(ApiResponse<IEnumerable<RideSearchResultDto>>.Success(
            results,
            "Ride search completed."));
    }
}