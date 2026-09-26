using Microsoft.AspNetCore.SignalR;
using PakiCarpool.API.Hubs;

namespace PakiCarpool.API.Helpers;

/// <summary>
/// Bridges <see cref="IHubContext{PakiCarpoolHub}"/> to <see cref="IHubContext{Hub}"/>
/// so the Services layer can broadcast without referencing the API project.
/// </summary>
internal sealed class HubContextBridge : IHubContext<Hub>
{
    private readonly IHubContext<PakiCarpoolHub> _inner;

    public HubContextBridge(IHubContext<PakiCarpoolHub> inner)
    {
        _inner = inner;
    }

    public IHubClients Clients => _inner.Clients;

    public IGroupManager Groups => _inner.Groups;
}
