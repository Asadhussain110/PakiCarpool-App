using Dapper;
using PakiCarpool.Domain.DTOs.Passenger;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Retrieves upcoming ride stops within range of a passenger's start or end coordinates.
/// </summary>
public sealed class RideSearchQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public RideSearchQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns nearby stop candidates and computes both coordinate distances in PostgreSQL.
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

        const string sql = """
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
            ), ranked_stops AS (
                SELECT
                    sd.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY sd."RideId"
                        ORDER BY sd."StartDistanceMeters", sd."SequenceOrder"
                    ) AS "StartRank",
                    ROW_NUMBER() OVER (
                        PARTITION BY sd."RideId"
                        ORDER BY sd."EndDistanceMeters", sd."SequenceOrder" DESC
                    ) AS "EndRank"
                FROM stop_distances sd
            ), ride_segments AS (
                SELECT
                    start_candidate."RideId",
                    start_candidate."SequenceOrder" AS "StartSequenceOrder",
                    end_candidate."SequenceOrder" AS "EndSequenceOrder",
                    start_candidate."MaxSeats" AS "MaxSeats"
                FROM ranked_stops start_candidate
                INNER JOIN ranked_stops end_candidate
                    ON end_candidate."RideId" = start_candidate."RideId"
                WHERE start_candidate."StartRank" = 1
                  AND start_candidate."StartDistanceMeters" <= @RadiusMeters
                  AND end_candidate."EndRank" = 1
                  AND end_candidate."EndDistanceMeters" <= @RadiusMeters
                  AND end_candidate."SequenceOrder" > start_candidate."SequenceOrder"
            ), seat_capacity AS (
                SELECT
                    segment."RideId",
                    segment."StartSequenceOrder",
                    segment."EndSequenceOrder",
                    segment."MaxSeats" - COUNT(CASE
                        WHEN pickup."SequenceOrder" < segment."EndSequenceOrder"
                         AND dropoff."SequenceOrder" > segment."StartSequenceOrder"
                        THEN booking."Id"
                    END)::integer AS "AvailableSeats"
                FROM ride_segments segment
                LEFT JOIN "Bookings" booking
                    ON booking."RideId" = segment."RideId"
                   AND booking."Status" IN ('Accepted', 'Active')
                LEFT JOIN "RideStops" pickup ON pickup."Id" = booking."PickupStopId"
                LEFT JOIN "RideStops" dropoff ON dropoff."Id" = booking."DropoffStopId"
                GROUP BY
                    segment."RideId",
                    segment."StartSequenceOrder",
                    segment."EndSequenceOrder",
                    segment."MaxSeats"
            )
            SELECT
                sd.*,
                COALESCE(capacity."AvailableSeats", sd."MaxSeats") AS "AvailableSeats"
            FROM stop_distances sd
            LEFT JOIN seat_capacity capacity ON capacity."RideId" = sd."RideId"
            WHERE sd."StartDistanceMeters" <= @RadiusMeters
               OR sd."EndDistanceMeters" <= @RadiusMeters
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