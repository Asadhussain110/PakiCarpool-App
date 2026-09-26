using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Rating endpoints for drivers and passengers (post cash-received).
/// </summary>
[ApiController]
[Authorize(Policy = RolePolicy.PassengerOrDriverPolicy)]
public sealed class RatingController : ApiControllerBase
{
    private readonly IRatingService _ratingService;

    public RatingController(IRatingService ratingService)
    {
        _ratingService = ratingService;
    }

    /// <summary>POST /driver/ratings — Driver rates passenger.</summary>
    [HttpPost("api/v1/driver/ratings")]
    [Authorize(Policy = RolePolicy.DriverPolicy)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RatePassenger([FromBody] RatingDto dto)
        => FromResponse(await _ratingService.SubmitRatingAsync(GetUserId(), dto));

    /// <summary>POST /passenger/ratings — Passenger rates driver.</summary>
    [HttpPost("api/v1/passenger/ratings")]
    [Authorize(Policy = RolePolicy.PassengerPolicy)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RateDriver([FromBody] RatingDto dto)
        => FromResponse(await _ratingService.SubmitRatingAsync(GetUserId(), dto));
}
