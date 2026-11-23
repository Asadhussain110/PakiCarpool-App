using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLivenessChallengeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LivenessChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(88)", maxLength: 88, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LivenessChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LivenessChallenges_Users_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LivenessChallenges_DriverId",
                table: "LivenessChallenges",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_LivenessChallenges_ExpiresAt",
                table: "LivenessChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_LivenessChallenges_Nonce",
                table: "LivenessChallenges",
                column: "Nonce",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LivenessChallenges");
        }
    }
}
