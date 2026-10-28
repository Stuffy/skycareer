using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AirportFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasLightedRunway",
                table: "Airports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "LongestPavedRunwayM",
                table: "Airports",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "ScheduledService",
                table: "Airports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AirportFacilities",
                columns: table => new
                {
                    Ident = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    HasIls = table.Column<bool>(type: "INTEGER", nullable: true),
                    IlsRunways = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AirportFacilities", x => x.Ident);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AirportFacilities");

            migrationBuilder.DropColumn(
                name: "HasLightedRunway",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "LongestPavedRunwayM",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "ScheduledService",
                table: "Airports");
        }
    }
}
