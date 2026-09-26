using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Health-check endpoint for load balancers and uptime monitors.
/// </summary>
[ApiController]
[Route("api/v1/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Returns API health status and UTC timestamp.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "PakiCarpool.API",
            timestamp = DateTime.UtcNow
        });
    }
}
