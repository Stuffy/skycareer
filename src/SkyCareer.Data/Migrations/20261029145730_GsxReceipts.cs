using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class GsxReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GsxCostsEnabled",
                table: "Careers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "GsxCostsSinceUtc",
                table: "Careers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GsxReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Icao = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    AmountEur = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Lines = table.Column<string>(type: "TEXT", nullable: false),
                    AircraftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LedgerEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoredHtml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GsxReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GsxReceipts_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GsxReceipts_CareerId_ReceiptId",
                table: "GsxReceipts",
                columns: new[] { "CareerId", "ReceiptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GsxReceipts");

            migrationBuilder.DropColumn(
                name: "GsxCostsEnabled",
                table: "Careers");

            migrationBuilder.DropColumn(
                name: "GsxCostsSinceUtc",
                table: "Careers");
        }
    }
}
