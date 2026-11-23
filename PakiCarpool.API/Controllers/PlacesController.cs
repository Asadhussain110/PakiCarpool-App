using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Places;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;
using PakiCarpool.Services.Implementations;
using PakiCarpool.Services.Options;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Passenger and driver place autocomplete endpoint.
/// Base route: /api/v1/places
/// </summary>
[Route("api/v1/places")]
[Authorize(Policy = RolePolicy.PassengerOrDriverPolicy)]
public sealed class PlacesController : ApiControllerBase
{
    private readonly IPlacesService _placesService;
    private readonly PhotonOptions _options;

    public PlacesController(IPlacesService placesService, IOptions<PhotonOptions> options)
    {
        _placesService = placesService;
        _options = options.Value;
    }

    /// <summary>GET /api/v1/places/autocomplete — Search for address suggestions.</summary>
    [HttpGet("autocomplete")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PlaceSuggestionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Autocomplete(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int? limit,
        [FromQuery(Name = "lang")] string? language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                Common.Constants.ErrorCodes.ValidationError,
                "A search query is required."));

        var effectiveLimit = limit ?? _options.DefaultLimit;
        if (effectiveLimit < 1 || effectiveLimit > _options.DefaultLimit)
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                Common.Constants.ErrorCodes.ValidationError,
                $"Limit must be between 1 and {_options.DefaultLimit}."));

        try
        {
            var suggestions = await _placesService.AutocompleteAsync(
                query.Trim(), effectiveLimit, language, cancellationToken);
            return FromResponse(ApiResponse<IEnumerable<PlaceSuggestionDto>>.Success(
                suggestions,
                "Place suggestions retrieved."));
        }
        catch (PlacesUnavailableException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ErrorResponse.Create(
                    StatusCodes.Status503ServiceUnavailable,
                    Common.Constants.ErrorCodes.InternalError,
                    "Place search is unavailable. Start Photon on the backend PC and try again."));
        }
    }

    /// <summary>GET /api/v1/places/reverse — Resolve coordinates to a place.</summary>
    [HttpGet("reverse")]
    [ProducesResponseType(typeof(ApiResponse<PlaceSuggestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reverse(
        [FromQuery(Name = "lat")] double? latitude,
        [FromQuery(Name = "lon")] double? longitude,
        CancellationToken cancellationToken)
    {
        if (latitude is not double lat || longitude is not double lon ||
            !double.IsFinite(lat) || !double.IsFinite(lon) ||
            lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                Common.Constants.ErrorCodes.ValidationError,
                "Valid latitude and longitude coordinates are required."));
        }

        try
        {
            var suggestion = await _placesService.ReverseAsync(lat, lon, cancellationToken);
            return FromResponse(ApiResponse<PlaceSuggestionDto?>.Success(
                suggestion,
                suggestion is null ? "No place was found for these coordinates." : "Place retrieved."));
        }
        catch (PlacesUnavailableException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ErrorResponse.Create(
                    StatusCodes.Status503ServiceUnavailable,
                    Common.Constants.ErrorCodes.InternalError,
                    "Place lookup is unavailable. Start Photon on the backend PC and try again."));
        }
    }
}