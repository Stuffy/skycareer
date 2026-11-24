using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class Clients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChainId",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChainStep",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientStandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Standing = table.Column<int>(type: "INTEGER", nullable: false),
                    FlightsFlown = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesTarget = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesFlown = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesEndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SeriesPayouts = table.Column<string>(type: "TEXT", nullable: false),
                    ChainId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ChainStep = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedChains = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientStandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientStandings_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientStandings_CareerId_ClientId",
                table: "ClientStandings",
                columns: new[] { "CareerId", "ClientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientStandings");

            migrationBuilder.DropColumn(
                name: "ChainId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ChainStep",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Contracts");
        }
    }
}
