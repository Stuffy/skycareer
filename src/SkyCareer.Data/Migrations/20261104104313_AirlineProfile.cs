using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AirlineProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FlightNumber",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlightNumber",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AirlineTelephony",
                table: "Careers",
                type: "TEXT",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlightNumber",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "FlightNumber",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "AirlineTelephony",
                table: "Careers");
        }
    }
}
