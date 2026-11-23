using Microsoft.AspNetCore.Authorization;
using PakiCarpool.Common.Constants;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Security.Policies;

/// <summary>
/// Authorization policy definitions for role-based access control.
/// Roles are immutable (set at registration) — Passenger, Driver, Admin.
/// </summary>
public static class RolePolicy
{
    public const string PassengerPolicy = "PassengerOnly";
    public const string DriverPolicy = "DriverOnly";
    public const string AdminPolicy = "AdminOnly";
    public const string PassengerOrDriverPolicy = "PassengerOrDriver";
    public const string SignalRPolicy = "SignalRAuthenticated";

    /// <summary>
    /// Policy for liveness endpoints. Requires a driver partial token pending verification.
    /// </summary>
    public const string RequiresLivenessPending = "RequiresLivenessPending";

    /// <summary>
    /// Registers all role-based authorization policies.
    /// Call this in Program.cs: builder.Services.AddAuthorization(RolePolicy.Configure);
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(PassengerPolicy, policy =>
            policy.RequireRole(RoleNames.Passenger));

        options.AddPolicy(DriverPolicy, policy =>
            policy.RequireRole(RoleNames.Driver)
                  .RequireAssertion(context =>
                      !context.User.HasClaim(JwtHelper.LivenessPendingClaim, "true")));

        options.AddPolicy(AdminPolicy, policy =>
            policy.RequireRole(RoleNames.Admin)
                  .RequireAssertion(context => !context.User.HasClaim("adminStep1", "true")));

        options.AddPolicy(PassengerOrDriverPolicy, policy =>
            policy.RequireRole(RoleNames.Passenger, RoleNames.Driver)
                  .RequireAssertion(context =>
                      !context.User.HasClaim(JwtHelper.LivenessPendingClaim, "true")));

        options.AddPolicy(SignalRPolicy, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireAssertion(context =>
                      !context.User.HasClaim(JwtHelper.LivenessPendingClaim, "true")));

        options.AddPolicy(RequiresLivenessPending, policy =>
            policy.RequireRole(RoleNames.Driver)
                  .RequireClaim(JwtHelper.LivenessPendingClaim, "true"));
    }
}
