using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PakiCarpool.Repository.EFCore;

#nullable disable

namespace PakiCarpool.Repository.Migrations;

[DbContext(typeof(PakiCarpoolDbContext))]
[Migration("20261010172000_AddPasswordResetCodeAttempts")]
public partial class AddPasswordResetCodeAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PasswordResetAttempts",
            table: "Users",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "PasswordResetRequestCount",
            table: "Users",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "PasswordResetRequestWindowStart",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PasswordResetAttempts", table: "Users");
        migrationBuilder.DropColumn(name: "PasswordResetRequestCount", table: "Users");
        migrationBuilder.DropColumn(name: "PasswordResetRequestWindowStart", table: "Users");
    }
}
