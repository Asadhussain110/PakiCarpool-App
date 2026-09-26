using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper query for per-segment seat capacity validation.
/// Counts overlapping accepted/active bookings between a requested pickup and dropoff stop pair.
/// This is the critical race-condition-safe check that prevents overbooking on any segment.
/// </summary>
public class SeatCapacityQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public SeatCapacityQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns the count of existing accepted/active bookings whose segments overlap
    /// with the requested pickup→dropoff segment. A booking overlaps if its pickup
    /// is before the requested dropoff AND its dropoff is after the requested pickup.
    /// </summary>
    /// <param name="rideId">The ride to check capacity for.</param>
    /// <param name="requestedPickupOrder">SequenceOrder of the requested pickup stop.</param>
    /// <param name="requestedDropoffOrder">SequenceOrder of the requested dropoff stop.</param>
    /// <returns>Count of overlapping bookings occupying seats on the contested segments.</returns>
    public async Task<int> GetOverlappingBookingCountAsync(
        Guid rideId,
        int requestedPickupOrder,
        int requestedDropoffOrder)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM "Bookings" b
            INNER JOIN "RideStops" pickup ON b."PickupStopId" = pickup."Id"
            INNER JOIN "RideStops" dropoff ON b."DropoffStopId" = dropoff."Id"
            WHERE b."RideId" = @RideId
              AND b."Status" IN ('Accepted', 'Active')
              AND pickup."SequenceOrder" < @RequestedDropoffOrder
              AND dropoff."SequenceOrder" > @RequestedPickupOrder
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(sql, new
        {
            RideId = rideId,
            RequestedPickupOrder = requestedPickupOrder,
            RequestedDropoffOrder = requestedDropoffOrder
        });
    }
}
