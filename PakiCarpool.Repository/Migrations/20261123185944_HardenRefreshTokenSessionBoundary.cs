using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class HardenRefreshTokenSessionBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SessionIssuedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            // Legacy rows have no trustworthy signed access-token iat. Force reauthentication;
            // the backfill only satisfies the new non-null schema for these revoked rows.
            migrationBuilder.Sql(
                "UPDATE \"RefreshTokens\" SET \"IsRevoked\" = TRUE;");

            migrationBuilder.Sql(
                "UPDATE \"RefreshTokens\" SET \"SessionIssuedAt\" = \"CreatedAt\";");

            migrationBuilder.AlterColumn<DateTime>(
                name: "SessionIssuedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionIssuedAt",
                table: "RefreshTokens");
        }
    }
}
