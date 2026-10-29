namespace SkyCareer.Domain.Career;

public enum Difficulty { Easy, Normal, Hard }

public enum Ownership { Owned, Leased, Rented }

public enum ContractStatus { Offered, Accepted, InProgress, Completed, Failed, Expired, Cancelled }

public enum FlightOutcome { Completed, Late, Diverted, Failed, Crashed, Cancelled }

public enum LedgerCategory
{
    MissionRevenue, Bonus, Penalty,
    Fuel, LandingFee, Handling, Navigation, Catering, Crew,
    Maintenance, Repair, Lease, Rent, Insurance,
    AircraftPurchase, AircraftSale, StartingCapital,
    /// <summary>Manual balance correction from the debug tools; not part of the operating result.</summary>
    Adjustment
}

/// <summary>
/// One career save. Game time only moves forward through flights and explicit waiting (flight-based time),
/// so a player who pauses for a week isn't charged for it.
/// </summary>
public sealed class Career
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string AirlineName { get; set; }
    public string? AirlineIcao { get; set; }
    public required string HomeBase { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public decimal Cash { get; set; }
    public int Reputation { get; set; } = 10;
    /// <summary>Game clock (UTC).</summary>
    public DateTime GameTime { get; set; }
    /// <summary>First day of the last month for which fixed costs were booked.</summary>
    public DateTime FixedCostsBookedThrough { get; set; }
    public string? SimBriefUser { get; set; }
    /// <summary>Load the aircraft in step with GSX boarding/deboarding instead of all at once.</summary>
    public bool GsxSyncEnabled { get; set; }
    /// <summary>Standard mass per passenger for new offers (default SimBrief's 175 lb).</summary>
    public double PassengerWeightKg { get; set; } = Loading.PassengerMassTable.SimBriefAdultKg;
    /// <summary>Mass of the one checked bag per passenger for new offers (default SimBrief's 50 lb).</summary>
    public double BagWeightKg { get; set; } = Loading.PassengerMassTable.SimBriefBagKg;
    /// <summary>
    /// Sends SimBrief the crew as passengers and bags, freight and mass differences as cargo, and varies bag weights per
    /// passenger. Needs bag weight 0 in the SimBrief airframe; off = SimBrief's standard model (fixed bag per passenger).
    /// </summary>
    public bool SimBriefDetailedLoad { get; set; }
    /// <summary>Book GSX receipts (handling, buses, catering, fuel) instead of the model's costs for them.</summary>
    public bool GsxCostsEnabled { get; set; }
    /// <summary>When the GSX cost option was switched on; older receipts are never booked.</summary>
    public DateTime? GsxCostsSinceUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FleetAircraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public required string ProfileId { get; set; }
    public required string Registration { get; set; }
    public Ownership Ownership { get; set; }
    public required string Location { get; set; }
    public double AirframeHours { get; set; }
    public int Cycles { get; set; }
    /// <summary>0–100. Wears with hours, drops on hard landings, restored by checks and repairs.</summary>
    public double Condition { get; set; } = 100;
    public double HoursSinceCheck { get; set; }
    /// <summary>Price paid (owned) – basis for resale value.</summary>
    public decimal PurchasePrice { get; set; }
    /// <summary>Monthly lease rate (leased only).</summary>
    public decimal MonthlyRate { get; set; }
    /// <summary>Rent per block hour (rented only).</summary>
    public decimal RentPerBlockHour { get; set; }
    public DateTime AcquiredAt { get; set; }
    public DateTime? LeaseEndsAt { get; set; }
    /// <summary>Aircraft is in maintenance (or being ferried) until this game time.</summary>
    public DateTime? UnavailableUntil { get; set; }
    /// <summary>Why the aircraft is unavailable, e.g. "Planmäßiger Check" or "Überführung nach LOWW"; null = maintenance.</summary>
    public string? UnavailableReason { get; set; }

    public bool IsAvailable(DateTime gameTime) => UnavailableUntil is null || UnavailableUntil <= gameTime;
}

/// <summary>A mission offer on the market or a job the player has taken. Load figures are frozen at offer time.</summary>
public sealed class MissionContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public Guid AircraftId { get; set; }
    public required string MissionTypeId { get; set; }
    public required string Origin { get; set; }
    public required string Destination { get; set; }
    public double DistanceNm { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Infants { get; set; }
    public double PassengerMassKg { get; set; }
    public double BaggageKg { get; set; }
    public double CargoKg { get; set; }
    public string CargoKind { get; set; } = "";
    public double PlannedBlockFuelKg { get; set; }
    public decimal Payout { get; set; }
    public DateTime OfferedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime Deadline { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Offered;
    /// <summary>Block fuel from an imported SimBrief OFP; overrides the app's own fuel plan.</summary>
    public double? OfpBlockFuelKg { get; set; }
    public double? OfpTripFuelKg { get; set; }
    public string? OfpRoute { get; set; }
    /// <summary>Real time (UTC) of the liftoff that started the job; used to resume tracking after an app restart.</summary>
    public DateTime? FlightStartedAtUtc { get; set; }
    /// <summary>Departure checks that were red at liftoff, one per line; each costs part of the payout at settlement.</summary>
    public string? DepartureIssues { get; set; }

    public int TotalPassengers => Adults + Children + Infants;
    public IReadOnlyList<string> DepartureIssueList =>
        string.IsNullOrEmpty(DepartureIssues) ? [] : DepartureIssues.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}

public sealed class FlightRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public Guid AircraftId { get; set; }
    public Guid ContractId { get; set; }
    public required string MissionTypeId { get; set; }
    public required string Origin { get; set; }
    public required string Destination { get; set; }
    public string? ArrivedAt { get; set; }
    public DateTime DepartedGameTime { get; set; }
    public DateTime ArrivedGameTime { get; set; }
    public double BlockHours { get; set; }
    public double AirHours { get; set; }
    public double DistanceNm { get; set; }
    public double FuelUsedKg { get; set; }
    public double TouchdownFpm { get; set; }
    public double MaxGForce { get; set; }
    public int Score { get; set; }
    public decimal Revenue { get; set; }
    public decimal Costs { get; set; }
    public FlightOutcome Outcome { get; set; }
    public string Notes { get; set; } = "";
    public string? TrackFile { get; set; }
}

public sealed class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public DateTime GameTime { get; set; }
    public LedgerCategory Category { get; set; }
    /// <summary>Positive = income, negative = expense.</summary>
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public Guid? AircraftId { get; set; }
    public Guid? FlightId { get; set; }
}

/// <summary>A GSX receipt booked into the ledger; the unique (CareerId, ReceiptId) keeps it from being booked twice.</summary>
public sealed class GsxReceiptRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public required string ReceiptId { get; set; }
    public LedgerCategory Category { get; set; }
    public required string Icao { get; set; }
    public string Operator { get; set; } = "";
    public required string Title { get; set; }
    public decimal AmountEur { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>Item and tax lines of the receipt, one per line.</summary>
    public string Lines { get; set; } = "";
    public Guid? AircraftId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid LedgerEntryId { get; set; }
    /// <summary>Copy of the GSX HTML invoice next to the database, if GSX wrote one.</summary>
    public string? StoredHtml { get; set; }
}
