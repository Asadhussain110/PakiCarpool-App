using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace PakiCarpool.API.Helpers;

/// <summary>
/// Maps SignalR connections to the JWT userId claim for group targeting.
/// </summary>
public sealed class NameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("userId")?.Value
               ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? connection.User?.FindFirst("sub")?.Value;
    }
}
