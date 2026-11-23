using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PakiCarpool.API.Controllers;
using PakiCarpool.API.Hubs;
using PakiCarpool.Common.Constants;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Security.Policies;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class PartialTokenAuthorizationTests
{
    [Fact]
    public async Task AdminPolicy_RejectsAdminStep1Token()
    {
        var principal = CreatePartialTokenPrincipal(RoleNames.Admin, "adminStep1");

        var result = await AuthorizeAsync(principal, RolePolicy.AdminPolicy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(RolePolicy.DriverPolicy)]
    [InlineData(RolePolicy.PassengerOrDriverPolicy)]
    public async Task OrdinaryDriverPolicies_RejectLivenessPendingToken(string policy)
    {
        var principal = CreatePartialTokenPrincipal(RoleNames.Driver, JwtHelper.LivenessPendingClaim);

        var result = await AuthorizeAsync(principal, policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignalRPolicy_RejectsLivenessPendingToken()
    {
        var principal = CreatePartialTokenPrincipal(RoleNames.Driver, JwtHelper.LivenessPendingClaim);

        var result = await AuthorizeAsync(principal, RolePolicy.SignalRPolicy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task LivenessPendingPolicy_AllowsLivenessVerification()
    {
        var principal = CreatePartialTokenPrincipal(RoleNames.Driver, JwtHelper.LivenessPendingClaim);

        var result = await AuthorizeAsync(principal, RolePolicy.RequiresLivenessPending);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ProtectedEndpoints_UseCompletedTokenPolicies()
    {
        Assert.Equal(
            RolePolicy.AdminPolicy,
            typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal(
            RolePolicy.DriverPolicy,
            typeof(DriverController).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal(
            RolePolicy.SignalRPolicy,
            typeof(PakiCarpoolHub).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void LivenessController_DoesNotRequireCompletedDriverToken()
    {
        var authorize = typeof(LivenessController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(RolePolicy.RequiresLivenessPending, authorize?.Policy);
    }

    private static ClaimsPrincipal CreatePartialTokenPrincipal(string role, string partialClaim)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = new string('t', 64),
                ["Auth:PartialTokenExpiryMinutes"] = "10"
            })
            .Build();
        var jwtHelper = new JwtHelper(configuration);
        var token = jwtHelper.GeneratePartialToken(
            Guid.NewGuid(),
            "partial-token-test@example.com",
            role,
            partialClaim,
            authTokenVersion: role == RoleNames.Admin ? 0 : null);

        return jwtHelper.ValidateToken(token)
            ?? throw new InvalidOperationException("The test partial token could not be validated.");
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal principal,
        string policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(RolePolicy.Configure);
        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();

        return await authorizationService.AuthorizeAsync(principal, policy);
    }
}
