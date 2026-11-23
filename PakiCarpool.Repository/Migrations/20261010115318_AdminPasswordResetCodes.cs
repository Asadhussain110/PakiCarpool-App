using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AdminPasswordResetCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminPasswordResetAttempts",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPasswordResetCodeExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminPasswordResetCodeHash",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdminPasswordResetRequestCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPasswordResetRequestWindowStart",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthTokenVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminPasswordResetAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdminPasswordResetCodeExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdminPasswordResetCodeHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdminPasswordResetRequestCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdminPasswordResetRequestWindowStart",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthTokenVersion",
                table: "Users");
        }
    }
}
