using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaIntegritySafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF does not model expression indexes. Keep the existing raw-email unique
            // index and add the stronger case-insensitive uniqueness guarantee.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_Users_Email_Lower"
                ON "Users" (LOWER("Email"));
                """);

            // ix_bookings_active_passenger and ix_rides_active_driver already exist
            // through earlier raw SQL migrations. Their model metadata is updated by
            // this migration, but no create/drop operation is emitted for them.
            migrationBuilder.AddCheckConstraint(
                name: "CK_RideStops_Latitude",
                table: "RideStops",
                sql: "\"Latitude\" >= -90 AND \"Latitude\" <= 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RideStops_Longitude",
                table: "RideStops",
                sql: "\"Longitude\" >= -180 AND \"Longitude\" <= 180");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rides_Fare",
                table: "Rides",
                sql: "\"Fare\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rides_MaxSeats",
                table: "Rides",
                sql: "\"MaxSeats\" >= 1 AND \"MaxSeats\" <= 6");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_RateeId",
                table: "Ratings",
                column: "RateeId");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_RaterId",
                table: "Ratings",
                column: "RaterId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ratings_Stars",
                table: "Ratings",
                sql: "\"Stars\" >= 1 AND \"Stars\" <= 5");

            migrationBuilder.CreateIndex(
                name: "IX_NoShowLogs_ReporterId",
                table: "NoShowLogs",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_NoShowLogs_RideId",
                table: "NoShowLogs",
                column: "RideId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_ReporteeId",
                table: "Flags",
                column: "ReporteeId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_ReporterId",
                table: "Flags",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_RideId",
                table: "Flags",
                column: "RideId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverLocations_RideId",
                table: "DriverLocations",
                column: "RideId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DriverLocations_Latitude",
                table: "DriverLocations",
                sql: "\"Latitude\" >= -90 AND \"Latitude\" <= 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DriverLocations_Longitude",
                table: "DriverLocations",
                sql: "\"Longitude\" >= -180 AND \"Longitude\" <= 180");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ReceiverId",
                table: "ChatMessages",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SenderId",
                table: "ChatMessages",
                column: "SenderId");

            migrationBuilder.AddForeignKey(
                name: "FK_CancellationLogs_Users_UserId",
                table: "CancellationLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_ReceiverId",
                table: "ChatMessages",
                column: "ReceiverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_SenderId",
                table: "ChatMessages",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DriverLocations_DriverProfiles_DriverId",
                table: "DriverLocations",
                column: "DriverId",
                principalTable: "DriverProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DriverLocations_Rides_RideId",
                table: "DriverLocations",
                column: "RideId",
                principalTable: "Rides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Rides_RideId",
                table: "Flags",
                column: "RideId",
                principalTable: "Rides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Users_ReporteeId",
                table: "Flags",
                column: "ReporteeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Users_ReporterId",
                table: "Flags",
                column: "ReporterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NoShowLogs_Bookings_BookingId",
                table: "NoShowLogs",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NoShowLogs_Rides_RideId",
                table: "NoShowLogs",
                column: "RideId",
                principalTable: "Rides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NoShowLogs_Users_OffenderId",
                table: "NoShowLogs",
                column: "OffenderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NoShowLogs_Users_ReporterId",
                table: "NoShowLogs",
                column: "ReporterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ratings_Rides_RideId",
                table: "Ratings",
                column: "RideId",
                principalTable: "Rides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ratings_Users_RateeId",
                table: "Ratings",
                column: "RateeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ratings_Users_RaterId",
                table: "Ratings",
                column: "RaterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CancellationLogs_Users_UserId",
                table: "CancellationLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_ReceiverId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_SenderId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_DriverLocations_DriverProfiles_DriverId",
                table: "DriverLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_DriverLocations_Rides_RideId",
                table: "DriverLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Rides_RideId",
                table: "Flags");

            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Users_ReporteeId",
                table: "Flags");

            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Users_ReporterId",
                table: "Flags");

            migrationBuilder.DropForeignKey(
                name: "FK_NoShowLogs_Bookings_BookingId",
                table: "NoShowLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_NoShowLogs_Rides_RideId",
                table: "NoShowLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_NoShowLogs_Users_OffenderId",
                table: "NoShowLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_NoShowLogs_Users_ReporterId",
                table: "NoShowLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Ratings_Rides_RideId",
                table: "Ratings");

            migrationBuilder.DropForeignKey(
                name: "FK_Ratings_Users_RateeId",
                table: "Ratings");

            migrationBuilder.DropForeignKey(
                name: "FK_Ratings_Users_RaterId",
                table: "Ratings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RideStops_Latitude",
                table: "RideStops");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RideStops_Longitude",
                table: "RideStops");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rides_Fare",
                table: "Rides");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rides_MaxSeats",
                table: "Rides");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_RateeId",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_RaterId",
                table: "Ratings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ratings_Stars",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_NoShowLogs_ReporterId",
                table: "NoShowLogs");

            migrationBuilder.DropIndex(
                name: "IX_NoShowLogs_RideId",
                table: "NoShowLogs");

            migrationBuilder.DropIndex(
                name: "IX_Flags_ReporteeId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_Flags_ReporterId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_Flags_RideId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_DriverLocations_RideId",
                table: "DriverLocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DriverLocations_Latitude",
                table: "DriverLocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DriverLocations_Longitude",
                table: "DriverLocations");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ReceiverId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_SenderId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email_Lower",
                table: "Users");
        }
    }
}
