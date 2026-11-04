using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarketSeedsAndFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MarketIndex",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketStream",
                table: "Contracts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketFilterJson",
                table: "Careers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MarketSeed",
                table: "Careers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CareerId_MarketStream",
                table: "Contracts",
                columns: new[] { "CareerId", "MarketStream" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_CareerId_MarketStream",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MarketIndex",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MarketStream",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MarketFilterJson",
                table: "Careers");

            migrationBuilder.DropColumn(
                name: "MarketSeed",
                table: "Careers");
        }
    }
}
