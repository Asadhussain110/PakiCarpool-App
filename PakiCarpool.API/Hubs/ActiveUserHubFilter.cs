using Microsoft.AspNetCore.SignalR;
using PakiCarpool.API.Helpers;
using PakiCarpool.Interfaces.IRepositories;

namespace PakiCarpool.API.Hubs;

/// <summary>
/// Rejects every hub invocation when the connected account has become inactive or suspended.
/// </summary>
public sealed class ActiveUserHubFilter : IHubFilter
{
    private readonly IUserRepository _userRepository;

    public ActiveUserHubFilter(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var user = await AccountAccessValidator.GetActiveUserAsync(
            invocationContext.Context.User,
            _userRepository,
            invocationContext.Context.ConnectionAborted);
        if (user is null)
            throw new HubException("Account is inactive or suspended.");

        return await next(invocationContext);
    }
}
