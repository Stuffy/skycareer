using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistFuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FuelKg",
                table: "Fleet",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FuelRestoredAtUtc",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PersistFuel",
                table: "Careers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // existing careers keep their fuel too
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FuelKg",
                table: "Fleet");

            migrationBuilder.DropColumn(
                name: "FuelRestoredAtUtc",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PersistFuel",
                table: "Careers");
        }
    }
}
