using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoUpdatedAtToDriverProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PhotoUpdatedAt",
                table: "DriverProfiles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoUpdatedAt",
                table: "DriverProfiles");
        }
    }
}
