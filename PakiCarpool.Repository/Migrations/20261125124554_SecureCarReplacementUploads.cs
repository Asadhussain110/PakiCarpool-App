using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class SecureCarReplacementUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByCarId",
                table: "DriverCars",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesCarId",
                table: "DriverCars",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_drivercars_pending_replacement",
                table: "DriverCars",
                column: "ReplacesCarId",
                unique: true,
                filter: "\"ReplacesCarId\" IS NOT NULL AND \"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_drivercars_pending_replacement",
                table: "DriverCars");

            migrationBuilder.DropColumn(
                name: "ReplacedByCarId",
                table: "DriverCars");

            migrationBuilder.DropColumn(
                name: "ReplacesCarId",
                table: "DriverCars");
        }
    }
}
