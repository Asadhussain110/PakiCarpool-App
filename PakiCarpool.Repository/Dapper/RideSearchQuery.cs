using Dapper;
using PakiCarpool.Domain.DTOs.Passenger;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Retrieves capacity-available, ordered ride stop pairs within range of a passenger's coordinates.
/// </summary>
public sealed class RideSearchQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public RideSearchQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns the two stops for every valid pair with a free seat on every route segment,
    /// and computes both coordinate distances and per-segment occupancy in PostgreSQL.
    /// </summary>
    public async Task<List<RideSearchCandidateDto>> GetCandidateRidesAsync(
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        double radiusMeters)
    {
        var radiusKm = radiusMeters / 1000.0;
        var latitudeDelta = radiusKm / 111.0;
        var startLongitudeDelta = radiusKm /
            (111.0 * Math.Max(Math.Abs(Math.Cos(startLat * Math.PI / 180.0)), 1e-12));
        var endLongitudeDelta = radiusKm /
            (111.0 * Math.Max(Math.Abs(Math.Cos(endLat * Math.PI / 180.0)), 1e-12));
        var minLat = Math.Min(startLat - latitudeDelta, endLat - latitudeDelta);
        var maxLat = Math.Max(startLat + latitudeDelta, endLat + latitudeDelta);
        var minLng = Math.Min(startLng - startLongitudeDelta, endLng - endLongitudeDelta);
        var maxLng = Math.Max(startLng + startLongitudeDelta, endLng + endLongitudeDelta);

        const string sql = $"""
            WITH stop_distances AS (
                SELECT
                    r."Id" AS "RideId",
                    r."DriverId" AS "DriverId",
                    u."FullName" AS "DriverName",
                    dp."ProfilePhotoPath" AS "DriverPhotoPath",
                    dp."PhotoUpdatedAt" AS "DriverPhotoUpdatedAt",
                    COALESCE(r."StartedAt", r."CreatedAt") AS "DepartureTime",
                    r."MaxSeats" AS "MaxSeats",
                    rs."Id" AS "StopId",
                    rs."Name" AS "StopName",
                    rs."Latitude" AS "Latitude",
                    rs."Longitude" AS "Longitude",
                    rs."SequenceOrder" AS "SequenceOrder",
                    6371000 * acos(LEAST(1.0::double precision, GREATEST(-1.0::double precision,
                        cos(radians(@StartLat)) * cos(radians(rs."Latitude")) *
                        cos(radians(rs."Longitude") - radians(@StartLng)) +
                        sin(radians(@StartLat)) * sin(radians(rs."Latitude"))
                    ))) AS "StartDistanceMeters",
                    6371000 * acos(LEAST(1.0::double precision, GREATEST(-1.0::double precision,
                        cos(radians(@EndLat)) * cos(radians(rs."Latitude")) *
                        cos(radians(rs."Longitude") - radians(@EndLng)) +
                        sin(radians(@EndLat)) * sin(radians(rs."Latitude"))
                    ))) AS "EndDistanceMeters"
                FROM "Rides" r
                INNER JOIN "RideStops" rs ON rs."RideId" = r."Id"
                INNER JOIN "DriverProfiles" dp ON dp."Id" = r."DriverId"
                INNER JOIN "Users" u ON u."Id" = dp."UserId"
                WHERE r."Status" IN ('Posted', 'Active')
                  AND rs."Status" = 'Upcoming'
                  AND rs."Latitude" BETWEEN @MinLat AND @MaxLat
                  AND rs."Longitude" BETWEEN @MinLng AND @MaxLng
            ), ride_pairs AS (
                SELECT
                    start_candidate."RideId" AS "RideId",
                    start_candidate."StopId" AS "StartStopId",
                    end_candidate."StopId" AS "EndStopId",
                    start_candidate."SequenceOrder" AS "StartSequenceOrder",
                    end_candidate."SequenceOrder" AS "EndSequenceOrder",
                    start_candidate."MaxSeats" AS "MaxSeats"
                FROM stop_distances start_candidate
                INNER JOIN stop_distances end_candidate
                    ON end_candidate."RideId" = start_candidate."RideId"
                WHERE start_candidate."StartDistanceMeters" <= @RadiusMeters
                  AND end_candidate."EndDistanceMeters" <= @RadiusMeters
                  AND end_candidate."SequenceOrder" > start_candidate."SequenceOrder"
            ), occupying_bookings AS (
                {SeatCapacityQuery.OccupyingBookingsSql}
            ), pair_segment_occupancy AS (
                SELECT
                    pair."RideId" AS "RideId",
                    pair."StartStopId" AS "StartStopId",
                    pair."EndStopId" AS "EndStopId",
                    route_segment."SequenceOrder" AS "SequenceOrder",
                    COUNT(booking."BookingId") AS "Occupancy"
                FROM ride_pairs pair
                INNER JOIN "RideStops" route_segment
                    ON route_segment."RideId" = pair."RideId"
                   AND route_segment."SequenceOrder" >= pair."StartSequenceOrder"
                   AND route_segment."SequenceOrder" < pair."EndSequenceOrder"
                LEFT JOIN occupying_bookings booking
                    ON booking."RideId" = pair."RideId"
                   AND booking."PickupOrder" <= route_segment."SequenceOrder"
                   AND booking."DropoffOrder" > route_segment."SequenceOrder"
                GROUP BY
                    pair."RideId",
                    pair."StartStopId",
                    pair."EndStopId",
                    route_segment."SequenceOrder"
            ), pair_capacity AS (
                SELECT
                    pair."RideId",
                    pair."StartStopId",
                    pair."EndStopId",
                    (pair."MaxSeats" - COALESCE(MAX(segment."Occupancy"), 0))::integer AS "AvailableSeats"
                FROM ride_pairs pair
                LEFT JOIN pair_segment_occupancy segment
                    ON segment."RideId" = pair."RideId"
                   AND segment."StartStopId" = pair."StartStopId"
                   AND segment."EndStopId" = pair."EndStopId"
                GROUP BY
                    pair."RideId",
                    pair."StartStopId",
                    pair."EndStopId",
                    pair."MaxSeats"
            )
            SELECT
                sd.*,
                pair."StartStopId" AS "PairStartStopId",
                pair."EndStopId" AS "PairEndStopId",
                pair."AvailableSeats" AS "AvailableSeats"
            FROM pair_capacity pair
            INNER JOIN stop_distances sd
                ON sd."RideId" = pair."RideId"
               AND (sd."StopId" = pair."StartStopId" OR sd."StopId" = pair."EndStopId")
            WHERE pair."AvailableSeats" > 0
            ORDER BY
                pair."RideId",
                pair."StartStopId",
                pair."EndStopId",
                sd."SequenceOrder"
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<RideSearchCandidateDto>(sql, new
        {
            StartLat = startLat,
            StartLng = startLng,
            EndLat = endLat,
            EndLng = endLng,
            RadiusMeters = radiusMeters,
            MinLat = minLat,
            MaxLat = maxLat,
            MinLng = minLng,
            MaxLng = maxLng
        });

        return rows.AsList();
    }
}
