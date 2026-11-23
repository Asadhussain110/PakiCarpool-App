using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class BookingRaceProtectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_bookings_active_passenger
                ON "Bookings" ("PassengerId")
                WHERE "Status" IN ('Pending', 'Accepted', 'Active');
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_rides_active_driver
                ON "Rides" ("DriverId")
                WHERE "Status" = 'Active';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_bookings_active_passenger;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_rides_active_driver;");
        }
    }
}
