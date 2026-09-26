using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper queries for ride analytics used by admin dashboard and reporting.
/// All queries are parameterized — zero string interpolation.
/// </summary>
public class RideAnalyticsQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public RideAnalyticsQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns aggregated dashboard metrics in a single round-trip.
    /// Counts: total users, drivers, passengers, rides, active rides, pending approvals, open flags, suspended accounts.
    /// </summary>
    public async Task<DashboardMetrics> GetDashboardMetricsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM "Users") AS "TotalUsers",
                (SELECT COUNT(*) FROM "Users" WHERE "Role" = 'Driver') AS "TotalDrivers",
                (SELECT COUNT(*) FROM "Users" WHERE "Role" = 'Passenger') AS "TotalPassengers",
                (SELECT COUNT(*) FROM "Rides") AS "TotalRides",
                (SELECT COUNT(*) FROM "Rides" WHERE "Status" IN ('Posted', 'Active')) AS "ActiveRides",
                (SELECT COUNT(*) FROM "DriverCars" WHERE "Status" = 'Pending') AS "PendingCarApprovals",
                (SELECT COUNT(*) FROM "Flags" WHERE "Status" = 'Open') AS "OpenFlags",
                (SELECT COUNT(*) FROM "Users" WHERE "IsSuspended" = true) AS "SuspendedAccounts"
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<DashboardMetrics>(sql);
    }

    /// <summary>
    /// Returns daily ride counts within a date range for trend analysis.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    public async Task<IEnumerable<DailyRideCount>> GetRideTrendsAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT
                DATE("CreatedAt") AS "Date",
                COUNT(*) AS "Count"
            FROM "Rides"
            WHERE "CreatedAt" >= @From AND "CreatedAt" < @To
            GROUP BY DATE("CreatedAt")
            ORDER BY DATE("CreatedAt")
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<DailyRideCount>(sql, new
        {
            From = from,
            To = to.AddDays(1) // Inclusive end date
        });
    }

    /// <summary>
    /// Returns the average number of bookings per ride within a date range.
    /// </summary>
    public async Task<decimal> GetAverageBookingsPerRideAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT COALESCE(AVG(booking_count), 0)
            FROM (
                SELECT r."Id", COUNT(b."Id") AS booking_count
                FROM "Rides" r
                LEFT JOIN "Bookings" b ON r."Id" = b."RideId"
                WHERE r."CreatedAt" >= @From AND r."CreatedAt" < @To
                GROUP BY r."Id"
            ) sub
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<decimal>(sql, new
        {
            From = from,
            To = to.AddDays(1)
        });
    }
}

/// <summary>
/// DTO for dashboard metrics returned by the analytics query.
/// </summary>
public class DashboardMetrics
{
    public int TotalUsers { get; set; }
    public int TotalDrivers { get; set; }
    public int TotalPassengers { get; set; }
    public int TotalRides { get; set; }
    public int ActiveRides { get; set; }
    public int PendingCarApprovals { get; set; }
    public int OpenFlags { get; set; }
    public int SuspendedAccounts { get; set; }
}

/// <summary>
/// DTO for daily ride count trend data.
/// </summary>
public class DailyRideCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
