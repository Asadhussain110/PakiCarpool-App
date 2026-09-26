using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper queries for live driver location aggregations.
/// Used for real-time map displays and proximity calculations.
/// All queries are parameterized — zero string interpolation.
/// </summary>
public class LiveLocationQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public LiveLocationQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns all active driver locations for rides with Posted or Active status.
    /// Used for admin dashboard map and passenger-facing ride discovery.
    /// </summary>
    public async Task<IEnumerable<ActiveDriverLocation>> GetActiveDriverLocationsAsync()
    {
        const string sql = """
            SELECT
                dl."DriverId",
                dl."RideId",
                dl."Latitude",
                dl."Longitude",
                dl."RecordedAt",
                u."FullName" AS "DriverName",
                r."Status" AS "RideStatus"
            FROM "DriverLocations" dl
            INNER JOIN "Rides" r ON dl."RideId" = r."Id"
            INNER JOIN "DriverProfiles" dp ON dl."DriverId" = dp."Id"
            INNER JOIN "Users" u ON dp."UserId" = u."Id"
            WHERE r."Status" IN ('Posted', 'Active')
            ORDER BY dl."RecordedAt" DESC
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<ActiveDriverLocation>(sql);
    }

    /// <summary>
    /// Returns driver locations within a bounding box defined by lat/lng range.
    /// Lightweight proximity filter for map viewport queries.
    /// </summary>
    /// <param name="minLat">Minimum latitude of bounding box.</param>
    /// <param name="maxLat">Maximum latitude of bounding box.</param>
    /// <param name="minLng">Minimum longitude of bounding box.</param>
    /// <param name="maxLng">Maximum longitude of bounding box.</param>
    public async Task<IEnumerable<ActiveDriverLocation>> GetDriversInBoundingBoxAsync(
        double minLat, double maxLat, double minLng, double maxLng)
    {
        const string sql = """
            SELECT
                dl."DriverId",
                dl."RideId",
                dl."Latitude",
                dl."Longitude",
                dl."RecordedAt",
                u."FullName" AS "DriverName",
                r."Status" AS "RideStatus"
            FROM "DriverLocations" dl
            INNER JOIN "Rides" r ON dl."RideId" = r."Id"
            INNER JOIN "DriverProfiles" dp ON dl."DriverId" = dp."Id"
            INNER JOIN "Users" u ON dp."UserId" = u."Id"
            WHERE r."Status" IN ('Posted', 'Active')
              AND dl."Latitude" BETWEEN @MinLat AND @MaxLat
              AND dl."Longitude" BETWEEN @MinLng AND @MaxLng
            ORDER BY dl."RecordedAt" DESC
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<ActiveDriverLocation>(sql, new
        {
            MinLat = minLat,
            MaxLat = maxLat,
            MinLng = minLng,
            MaxLng = maxLng
        });
    }

    /// <summary>
    /// Returns the latest location for a specific driver on a specific ride.
    /// </summary>
    public async Task<ActiveDriverLocation?> GetDriverLocationForRideAsync(Guid driverId, Guid rideId)
    {
        const string sql = """
            SELECT
                dl."DriverId",
                dl."RideId",
                dl."Latitude",
                dl."Longitude",
                dl."RecordedAt",
                u."FullName" AS "DriverName",
                r."Status" AS "RideStatus"
            FROM "DriverLocations" dl
            INNER JOIN "Rides" r ON dl."RideId" = r."Id"
            INNER JOIN "DriverProfiles" dp ON dl."DriverId" = dp."Id"
            INNER JOIN "Users" u ON dp."UserId" = u."Id"
            WHERE dl."DriverId" = @DriverId AND dl."RideId" = @RideId
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ActiveDriverLocation>(sql, new
        {
            DriverId = driverId,
            RideId = rideId
        });
    }
}

/// <summary>
/// DTO for active driver location results.
/// </summary>
public class ActiveDriverLocation
{
    public Guid DriverId { get; set; }
    public Guid RideId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string RideStatus { get; set; } = string.Empty;
}
