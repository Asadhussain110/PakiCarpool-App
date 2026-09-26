using Dapper;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Dapper queries for admin reports and data exports.
/// Generates ride, cancellation, and user activity report data for CSV/PDF export.
/// All queries are parameterized — zero string interpolation.
/// </summary>
public class ReportQuery
{
    private readonly IDapperConnectionFactory _connectionFactory;

    public ReportQuery(IDapperConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns ride report data within a date range.
    /// Includes ride details, driver info, booking count, and revenue.
    /// </summary>
    public async Task<IEnumerable<RideReportRow>> GetRideReportAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT
                r."Id" AS "RideId",
                u."FullName" AS "DriverName",
                u."Email" AS "DriverEmail",
                r."Fare",
                r."MaxSeats",
                r."Status",
                r."CreatedAt",
                (SELECT COUNT(*) FROM "Bookings" b WHERE b."RideId" = r."Id") AS "TotalBookings",
                (SELECT COUNT(*) FROM "Bookings" b WHERE b."RideId" = r."Id" AND b."Status" = 'Completed') AS "CompletedBookings",
                (SELECT COUNT(*) FROM "Bookings" b WHERE b."RideId" = r."Id" AND b."Status" = 'Cancelled') AS "CancelledBookings"
            FROM "Rides" r
            INNER JOIN "DriverProfiles" dp ON r."DriverId" = dp."Id"
            INNER JOIN "Users" u ON dp."UserId" = u."Id"
            WHERE r."CreatedAt" >= @From AND r."CreatedAt" < @To
            ORDER BY r."CreatedAt" DESC
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<RideReportRow>(sql, new
        {
            From = from,
            To = to.AddDays(1)
        });
    }

    /// <summary>
    /// Returns cancellation report data within a date range.
    /// Groups by classification (Valid vs Frivolous) with user details.
    /// </summary>
    public async Task<IEnumerable<CancellationReportRow>> GetCancellationReportAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT
                cl."Id",
                u."FullName" AS "UserName",
                u."Email" AS "UserEmail",
                u."Role",
                cl."EntityType",
                cl."Reason",
                cl."Classification",
                cl."TrustPointsDeducted",
                cl."CreatedAt"
            FROM "CancellationLogs" cl
            INNER JOIN "Users" u ON cl."UserId" = u."Id"
            WHERE cl."CreatedAt" >= @From AND cl."CreatedAt" < @To
            ORDER BY cl."CreatedAt" DESC
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<CancellationReportRow>(sql, new
        {
            From = from,
            To = to.AddDays(1)
        });
    }

    /// <summary>
    /// Returns user activity summary within a date range.
    /// Includes ride count, booking count, cancellation count, and trust score.
    /// </summary>
    public async Task<IEnumerable<UserActivityReportRow>> GetUserActivityReportAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT
                u."Id" AS "UserId",
                u."FullName",
                u."Email",
                u."Role",
                u."TrustScore",
                u."IsSuspended",
                (SELECT COUNT(*) FROM "CancellationLogs" cl
                    WHERE cl."UserId" = u."Id"
                    AND cl."CreatedAt" >= @From AND cl."CreatedAt" < @To) AS "Cancellations",
                (SELECT COUNT(*) FROM "NoShowLogs" ns
                    WHERE ns."OffenderId" = u."Id"
                    AND ns."CreatedAt" >= @From AND ns."CreatedAt" < @To) AS "NoShows"
            FROM "Users" u
            ORDER BY u."TrustScore" ASC
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<UserActivityReportRow>(sql, new
        {
            From = from,
            To = to.AddDays(1)
        });
    }

    /// <summary>
    /// Returns aggregated report summary for the date range.
    /// </summary>
    public async Task<ReportSummary> GetReportSummaryAsync(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM "Rides"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To) AS "TotalRides",
                (SELECT COUNT(*) FROM "Rides"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To AND "Status" = 'Completed') AS "CompletedRides",
                (SELECT COUNT(*) FROM "Rides"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To AND "Status" = 'Cancelled') AS "CancelledRides",
                (SELECT COUNT(*) FROM "Bookings"
                    WHERE "RequestedAt" >= @From AND "RequestedAt" < @To) AS "TotalBookings",
                (SELECT COUNT(*) FROM "Bookings"
                    WHERE "RequestedAt" >= @From AND "RequestedAt" < @To AND "Status" = 'Completed') AS "CompletedBookings",
                (SELECT COALESCE(SUM("Fare"), 0) FROM "Rides"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To AND "Status" = 'Completed') AS "TotalRevenue",
                (SELECT COUNT(*) FROM "Users"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To) AS "NewUsers",
                (SELECT COUNT(*) FROM "CancellationLogs"
                    WHERE "CreatedAt" >= @From AND "CreatedAt" < @To AND "Classification" = 'Frivolous') AS "FrivolousCancellations"
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<ReportSummary>(sql, new
        {
            From = from,
            To = to.AddDays(1)
        });
    }
}

/// <summary>
/// DTO for ride report row.
/// </summary>
public class RideReportRow
{
    public Guid RideId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string DriverEmail { get; set; } = string.Empty;
    public decimal Fare { get; set; }
    public int MaxSeats { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
}

/// <summary>
/// DTO for cancellation report row.
/// </summary>
public class CancellationReportRow
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public int TrustPointsDeducted { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for user activity report row.
/// </summary>
public class UserActivityReportRow
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int TrustScore { get; set; }
    public bool IsSuspended { get; set; }
    public int Cancellations { get; set; }
    public int NoShows { get; set; }
}

/// <summary>
/// DTO for aggregated report summary.
/// </summary>
public class ReportSummary
{
    public int TotalRides { get; set; }
    public int CompletedRides { get; set; }
    public int CancelledRides { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public decimal TotalRevenue { get; set; }
    public int NewUsers { get; set; }
    public int FrivolousCancellations { get; set; }
}
