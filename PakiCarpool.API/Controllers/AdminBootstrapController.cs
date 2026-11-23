using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// One-time first-admin enrollment. This endpoint is disabled after an admin exists.
/// </summary>
[Route("api/v1/auth/admin/bootstrap")]
public sealed class AdminBootstrapController : ApiControllerBase
{
    public const string SeedTokenHeaderName = "X-Admin-Seed-Token";

    private readonly IAdminBootstrapService _bootstrapService;

    public AdminBootstrapController(IAdminBootstrapService bootstrapService)
    {
        _bootstrapService = bootstrapService;
    }

    /// <summary>POST /auth/admin/bootstrap — Create the first admin and its TOTP enrollment.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AdminBootstrapResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Bootstrap(
        [FromBody] AdminBootstrapRequestDto dto,
        [FromHeader(Name = SeedTokenHeaderName)] string? seedToken,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _bootstrapService.BootstrapAsync(dto, seedToken, cancellationToken));
    }

    private IActionResult InvalidModel()
    {
        var errors = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage);

        return BadRequest(ErrorResponse.Create(
            StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationError,
            string.Join(" ", errors)));
    }
}
