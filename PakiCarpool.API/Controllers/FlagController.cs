using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Flag/report endpoints for drivers and passengers.
/// </summary>
[ApiController]
[Authorize(Policy = RolePolicy.PassengerOrDriverPolicy)]
public sealed class FlagController : ApiControllerBase
{
    private readonly IFlagService _flagService;

    public FlagController(IFlagService flagService)
    {
        _flagService = flagService;
    }

    /// <summary>POST /driver/flags — Driver flags a passenger.</summary>
    [HttpPost("api/v1/driver/flags")]
    [Authorize(Policy = RolePolicy.DriverPolicy)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FlagPassenger([FromBody] FlagDto dto, CancellationToken cancellationToken)
        => FromResponse(await _flagService.CreateFlagAsync(GetUserId(), dto, cancellationToken));

    /// <summary>POST /passenger/flags — Passenger flags a driver.</summary>
    [HttpPost("api/v1/passenger/flags")]
    [Authorize(Policy = RolePolicy.PassengerPolicy)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FlagDriver([FromBody] FlagDto dto, CancellationToken cancellationToken)
        => FromResponse(await _flagService.CreateFlagAsync(GetUserId(), dto, cancellationToken));
}
