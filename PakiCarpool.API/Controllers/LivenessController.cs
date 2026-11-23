using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
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
[Authorize(Policy = RolePolicy.RequiresLivenessPending)]
public sealed class LivenessController : ApiControllerBase
{
    private readonly ILivenessService _livenessService;
    private readonly ILogger<LivenessController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public LivenessController(
        ILivenessService livenessService,
        ILogger<LivenessController> logger,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _livenessService = livenessService;
        _logger = logger;
        _configuration = configuration;
        _env = env;
    }

    /// <summary>
    /// GET /driver/liveness/challenge — Issues a one-time nonce and ordered server-selected prompts.
    /// The nonce expires after <c>Liveness:NonceTtlMinutes</c> minutes (default: 2).
    /// Embed the returned nonce and prompt evidence in the subsequent liveness submission body.
    /// Auth: <see cref="RolePolicy.RequiresLivenessPending"/> (Driver JWT with liveness pending).
    /// </summary>
    [HttpGet("liveness/challenge")]
    [Authorize(Policy = RolePolicy.RequiresLivenessPending)]
    [ProducesResponseType(typeof(LivenessChallengeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChallenge(CancellationToken cancellationToken)
    {
        var dto = await _livenessService.IssueChallengeAsync(GetUserId(), cancellationToken);
        return Ok(new { nonce = dto.Nonce, prompts = dto.Prompts, expiresAt = dto.ExpiresAt });
    }

    [HttpGet("liveness/status")]
    [Authorize(Policy = RolePolicy.RequiresLivenessPending)]
    [ProducesResponseType(typeof(LivenessSubmissionResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewStatus(CancellationToken cancellationToken)
        => Ok(await _livenessService.GetReviewStatusAsync(GetUserId(), cancellationToken));

    /// <summary>POST /driver/liveness — Submit liveness result + selfie.</summary>
    [HttpPost("liveness")]
    [Authorize(Policy = RolePolicy.RequiresLivenessPending)]
    [ProducesResponseType(typeof(LivenessSubmissionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> SubmitLiveness([FromBody] LivenessResultDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _livenessService.ProcessLivenessResultAsync(GetUserId(), dto, cancellationToken);
            if (response.Success && response.RawRefreshToken is not null)
            {
                RefreshTokenCookie.Append(
                    Response,
                    response.RawRefreshToken,
                    _configuration.GetValue<int>("Auth:RefreshTokenExpiryDays"),
                    !_env.IsDevelopment());
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while processing liveness submission.");
            return StatusCode(500, new ErrorResponse
            {
                StatusCode = 500,
                Error = "INTERNAL_ERROR",
                Message = "An unexpected error occurred."
            });
        }
    }
}
