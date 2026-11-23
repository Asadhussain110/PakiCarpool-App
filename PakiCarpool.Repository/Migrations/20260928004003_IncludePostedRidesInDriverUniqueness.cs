using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class IncludePostedRidesInDriverUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_rides_active_driver;");
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_rides_active_driver
                ON "Rides" ("DriverId")
                WHERE "Status" IN ('Posted', 'Active');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_rides_active_driver;");
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_rides_active_driver
                ON "Rides" ("DriverId")
                WHERE "Status" = 'Active';
                """);
        }
    }
}
