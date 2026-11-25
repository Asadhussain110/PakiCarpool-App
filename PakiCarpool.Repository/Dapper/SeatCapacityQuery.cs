using System.Data;
using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper query for per-segment seat capacity validation.
/// Computes the maximum pending/accepted/active occupancy across the requested route segments.
/// Atomic booking acceptance runs this query on the accepting transaction's connection.
/// </summary>
public class SeatCapacityQuery
{
    internal const string OccupyingBookingsSql = """
        SELECT
            b."Id" AS "BookingId",
            b."RideId" AS "RideId",
            pickup."SequenceOrder" AS "PickupOrder",
            dropoff."SequenceOrder" AS "DropoffOrder"
        FROM "Bookings" b
        INNER JOIN "RideStops" pickup ON b."PickupStopId" = pickup."Id"
        INNER JOIN "RideStops" dropoff ON b."DropoffStopId" = dropoff."Id"
        WHERE b."Status" IN ('Pending', 'Accepted', 'Active')
        """;

    private const string MaximumOccupancySql = $"""
        WITH route_segments AS (
            SELECT rs."SequenceOrder"
            FROM "RideStops" rs
            WHERE rs."RideId" = @RideId
              AND rs."SequenceOrder" >= @RequestedPickupOrder
              AND rs."SequenceOrder" < @RequestedDropoffOrder
        ), occupying_bookings AS (
            SELECT bookings.*
            FROM ({OccupyingBookingsSql}) bookings
            WHERE bookings."RideId" = @RideId
              AND (@ExcludedBookingId IS NULL OR bookings."BookingId" <> @ExcludedBookingId)
        ), segment_occupancy AS (
            SELECT
                segment."SequenceOrder",
                COUNT(booking."BookingId") AS "Occupancy"
            FROM route_segments segment
            LEFT JOIN occupying_bookings booking
                ON booking."PickupOrder" <= segment."SequenceOrder"
               AND booking."DropoffOrder" > segment."SequenceOrder"
            GROUP BY segment."SequenceOrder"
        )
        SELECT COALESCE(MAX("Occupancy"), 0)::integer
        FROM segment_occupancy
        """;

    private readonly IDapperConnectionFactory _connectionFactory;

    public SeatCapacityQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns the maximum number of existing pending/accepted/active bookings occupying
    /// any single route segment in the requested pickup→dropoff range.
    /// </summary>
    /// <param name="rideId">The ride to check capacity for.</param>
    /// <param name="requestedPickupOrder">SequenceOrder of the requested pickup stop.</param>
    /// <param name="requestedDropoffOrder">SequenceOrder of the requested dropoff stop.</param>
    /// <returns>Maximum occupancy across the requested route segments.</returns>
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
    /// Computes maximum per-segment occupancy using the caller's connection and transaction.
    /// This keeps capacity validation in the same transaction as the booking status update.
    /// The booking being accepted is excluded because its Pending row represents the requested seat.
    /// </summary>
    public async Task<int> GetOverlappingBookingCountAsync(
        Guid rideId,
        int requestedPickupOrder,
        int requestedDropoffOrder,
        IDbConnection connection,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default,
        Guid? excludedBookingId = null)
    {
        var command = new CommandDefinition(
            MaximumOccupancySql,
            new
            {
                RideId = rideId,
                RequestedPickupOrder = requestedPickupOrder,
                RequestedDropoffOrder = requestedDropoffOrder,
                ExcludedBookingId = excludedBookingId
            },
            transaction,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }
}
