using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComponentWear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComponentWear",
                table: "Fleet",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SimWearAtStart",
                table: "Contracts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComponentWear",
                table: "Fleet");

            migrationBuilder.DropColumn(
                name: "SimWearAtStart",
                table: "Contracts");
        }
    }
}
