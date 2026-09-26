using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Driver liveness verification endpoints.
/// Routes: /api/v1/driver/liveness*
/// </summary>
[Route("api/v1/driver")]
[Authorize(Policy = RolePolicy.DriverPolicy)]
public sealed class LivenessController : ApiControllerBase
{
    private readonly ILivenessService _livenessService;

    public LivenessController(ILivenessService livenessService)
    {
        _livenessService = livenessService;
    }

    /// <summary>POST /driver/liveness — Submit liveness result + selfie.</summary>
    [HttpPost("liveness")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitLiveness([FromBody] LivenessResultDto dto)
        => FromResponse(await _livenessService.ProcessLivenessResultAsync(GetUserId(), dto));

    /// <summary>POST /driver/liveness/report-failure — Report consecutive failure.</summary>
    [HttpPost("liveness/report-failure")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportFailure()
        => FromResponse(await _livenessService.ReportLivenessFailureAsync(GetUserId()));
}
