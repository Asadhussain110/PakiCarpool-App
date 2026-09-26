using Microsoft.AspNetCore.Mvc;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Email delivery is invoked internally by Auth/Admin/Driver flows through Brevo's REST API.
/// No public HTTP endpoints are defined in the FRD/API reference.
/// </summary>
[ApiController]
[Route("api/v1/email")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EmailController : ControllerBase
{
    public EmailController(IEmailService emailService)
    {
        ArgumentNullException.ThrowIfNull(emailService);
    }
}
