using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RideStopStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "RideStops",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Upcoming");

            migrationBuilder.Sql(
                "UPDATE \"RideStops\" SET \"Status\" = CASE WHEN \"IsCompleted\" THEN 'Completed' ELSE 'Upcoming' END;");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "RideStops");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "RideStops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE \"RideStops\" SET \"IsCompleted\" = (\"Status\" = 'Completed');");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RideStops");
        }
    }
}
