using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCareer.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Airports",
                columns: table => new
                {
                    Ident = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Size = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LongestRunwayM = table.Column<double>(type: "REAL", nullable: false),
                    Surfaces = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    Municipality = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Airports", x => x.Ident);
                });

            migrationBuilder.CreateTable(
                name: "Careers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AirlineName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AirlineIcao = table.Column<string>(type: "TEXT", nullable: true),
                    HomeBase = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Difficulty = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Cash = table.Column<string>(type: "TEXT", nullable: false),
                    Reputation = table.Column<int>(type: "INTEGER", nullable: false),
                    GameTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FixedCostsBookedThrough = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SimBriefUser = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Careers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AircraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionTypeId = table.Column<string>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    DistanceNm = table.Column<double>(type: "REAL", nullable: false),
                    Adults = table.Column<int>(type: "INTEGER", nullable: false),
                    Children = table.Column<int>(type: "INTEGER", nullable: false),
                    Infants = table.Column<int>(type: "INTEGER", nullable: false),
                    PassengerMassKg = table.Column<double>(type: "REAL", nullable: false),
                    BaggageKg = table.Column<double>(type: "REAL", nullable: false),
                    CargoKg = table.Column<double>(type: "REAL", nullable: false),
                    CargoKind = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedBlockFuelKg = table.Column<double>(type: "REAL", nullable: false),
                    Payout = table.Column<string>(type: "TEXT", nullable: false),
                    OfferedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OfpBlockFuelKg = table.Column<double>(type: "REAL", nullable: true),
                    OfpTripFuelKg = table.Column<double>(type: "REAL", nullable: true),
                    OfpRoute = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    Registration = table.Column<string>(type: "TEXT", nullable: false),
                    Ownership = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    AirframeHours = table.Column<double>(type: "REAL", nullable: false),
                    Cycles = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<double>(type: "REAL", nullable: false),
                    HoursSinceCheck = table.Column<double>(type: "REAL", nullable: false),
                    PurchasePrice = table.Column<string>(type: "TEXT", nullable: false),
                    MonthlyRate = table.Column<string>(type: "TEXT", nullable: false),
                    RentPerBlockHour = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseEndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnavailableUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fleet", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fleet_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AircraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionTypeId = table.Column<string>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    ArrivedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DepartedGameTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArrivedGameTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BlockHours = table.Column<double>(type: "REAL", nullable: false),
                    AirHours = table.Column<double>(type: "REAL", nullable: false),
                    DistanceNm = table.Column<double>(type: "REAL", nullable: false),
                    FuelUsedKg = table.Column<double>(type: "REAL", nullable: false),
                    TouchdownFpm = table.Column<double>(type: "REAL", nullable: false),
                    MaxGForce = table.Column<double>(type: "REAL", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Revenue = table.Column<string>(type: "TEXT", nullable: false),
                    Costs = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    TrackFile = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Flights_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CareerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Amount = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AircraftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FlightId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ledger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ledger_Careers_CareerId",
                        column: x => x.CareerId,
                        principalTable: "Careers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Airports_Latitude_Longitude",
                table: "Airports",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CareerId_Status",
                table: "Contracts",
                columns: new[] { "CareerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Fleet_CareerId",
                table: "Fleet",
                column: "CareerId");

            migrationBuilder.CreateIndex(
                name: "IX_Fleet_CareerId_Registration",
                table: "Fleet",
                columns: new[] { "CareerId", "Registration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flights_CareerId",
                table: "Flights",
                column: "CareerId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledger_CareerId_GameTime",
                table: "Ledger",
                columns: new[] { "CareerId", "GameTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Airports");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Fleet");

            migrationBuilder.DropTable(
                name: "Flights");

            migrationBuilder.DropTable(
                name: "Ledger");

            migrationBuilder.DropTable(
                name: "Careers");
        }
    }
}
