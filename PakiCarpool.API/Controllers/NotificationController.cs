using Microsoft.AspNetCore.Mvc;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Notifications are driven by domain events (SignalR + email matrix).
/// No public HTTP endpoints are defined in the FRD/API reference.
/// </summary>
[ApiController]
[Route("api/v1/notification")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class NotificationController : ControllerBase
{
    public NotificationController(INotificationService notificationService)
    {
        ArgumentNullException.ThrowIfNull(notificationService);
    }
}
