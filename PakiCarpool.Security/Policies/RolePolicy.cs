using Microsoft.AspNetCore.Authorization;
using PakiCarpool.Common.Constants;

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

    /// <summary>
    /// Registers all role-based authorization policies.
    /// Call this in Program.cs: builder.Services.AddAuthorization(RolePolicy.Configure);
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(PassengerPolicy, policy =>
            policy.RequireRole(RoleNames.Passenger));

        options.AddPolicy(DriverPolicy, policy =>
            policy.RequireRole(RoleNames.Driver));

        options.AddPolicy(AdminPolicy, policy =>
            policy.RequireRole(RoleNames.Admin));

        options.AddPolicy(PassengerOrDriverPolicy, policy =>
            policy.RequireRole(RoleNames.Passenger, RoleNames.Driver));
    }
}
