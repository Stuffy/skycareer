using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResumeAndGsxWait : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArmedAtUtc",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FuelAtBlockOffKg",
                table: "Contracts",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArmedAtUtc",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FuelAtBlockOffKg",
                table: "Contracts");
        }
    }
}
