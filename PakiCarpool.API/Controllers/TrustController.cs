using Microsoft.AspNetCore.Mvc;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Trust scoring is applied by Driver/Passenger/Admin flows and TrustScoreJob.
/// No public HTTP endpoints are defined in the FRD/API reference.
/// </summary>
[ApiController]
[Route("api/v1/trust")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TrustController : ControllerBase
{
    public TrustController(ITrustService trustService)
    {
        ArgumentNullException.ThrowIfNull(trustService);
    }
}
