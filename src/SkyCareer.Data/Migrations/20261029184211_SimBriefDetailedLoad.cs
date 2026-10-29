using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimBriefDetailedLoad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SimBriefDetailedLoad",
                table: "Careers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SimBriefDetailedLoad",
                table: "Careers");
        }
    }
}
