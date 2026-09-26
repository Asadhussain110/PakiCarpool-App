namespace PakiCarpool.Common.Constants;

/// <summary>
/// Role name constants matching the UserRole enum string values.
/// Used in JWT claims and authorization policies.
/// </summary>
public static class RoleNames
{
    public const string Passenger = "Passenger";
    public const string Driver = "Driver";
    public const string Admin = "Admin";
}
