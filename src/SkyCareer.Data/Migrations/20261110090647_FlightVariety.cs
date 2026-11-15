using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class FlightVariety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Comfort",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerComments",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemandEvent",
                table: "Contracts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comfort",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "PassengerComments",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "DemandEvent",
                table: "Contracts");
        }
    }
}
