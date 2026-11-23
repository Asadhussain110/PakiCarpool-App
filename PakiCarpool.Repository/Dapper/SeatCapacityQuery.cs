using System.Data;
using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper query for per-segment seat capacity validation.
/// Counts overlapping accepted/active bookings between a requested pickup and dropoff stop pair.
/// Atomic booking acceptance runs this query on the accepting transaction's connection.
/// </summary>
public class SeatCapacityQuery
{
    private const string OverlappingBookingCountSql = """
        SELECT COUNT(*)
        FROM "Bookings" b
        INNER JOIN "RideStops" pickup ON b."PickupStopId" = pickup."Id"
        INNER JOIN "RideStops" dropoff ON b."DropoffStopId" = dropoff."Id"
        WHERE b."RideId" = @RideId
          AND b."Status" IN ('Accepted', 'Active')
          AND pickup."SequenceOrder" < @RequestedDropoffOrder
          AND dropoff."SequenceOrder" > @RequestedPickupOrder
        """;

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
        using var connection = _connectionFactory.CreateConnection();
        return await GetOverlappingBookingCountAsync(
            rideId,
            requestedPickupOrder,
            requestedDropoffOrder,
            connection);
    }

    /// <summary>
    /// Counts overlapping accepted/active bookings using the caller's connection and transaction.
    /// This keeps capacity validation in the same transaction as the booking status update.
    /// </summary>
    public async Task<int> GetOverlappingBookingCountAsync(
        Guid rideId,
        int requestedPickupOrder,
        int requestedDropoffOrder,
        IDbConnection connection,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            OverlappingBookingCountSql,
            new
            {
                RideId = rideId,
                RequestedPickupOrder = requestedPickupOrder,
                RequestedDropoffOrder = requestedDropoffOrder
            },
            transaction,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }
}
