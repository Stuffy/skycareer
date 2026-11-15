using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions;

public enum SimBriefUsage { None, Optional, Required }

public enum FlightRulesRequirement { Any, Ifr, Vfr }

/// <summary>Which aircraft may fly a mission type. All conditions must hold; empty lists mean "no constraint".</summary>
public sealed record MissionRequirements
{
    /// <summary>Every tag must be present on the aircraft.</summary>
    public IReadOnlyList<string> RequiredTags { get; init; } = [];
    /// <summary>At least one of these tags must be present (e.g. "float" or "amphibian").</summary>
    public IReadOnlyList<string> AnyOfTags { get; init; } = [];
    /// <summary>None of these tags may be present.</summary>
    public IReadOnlyList<string> ForbiddenTags { get; init; } = [];
    public int MinSeats { get; init; }
    public double MinCargoKg { get; init; }
    public double MinRangeNm { get; init; }
    public double MaxMtowKg { get; init; }
    /// <summary>Aircraft must be able to operate on at least one of these surfaces.</summary>
    public IReadOnlyList<RunwaySurface> Surfaces { get; init; } = [];
}

/// <summary>
/// Passenger numbers. With <see cref="LoadFactor"/> the seated count leans towards a full cabin (mean seat occupancy,
/// normally spread by <see cref="LoadFactorSpread"/>); without it the count is drawn evenly between min and max.
/// </summary>
public sealed record PaxRule(int Min, int Max, double ChildShare = 0.05, double InfantShare = 0.01,
    BaggageProfile Baggage = BaggageProfile.European)
{
    public double? LoadFactor { get; init; }
    public double LoadFactorSpread { get; init; } = 0.1;
}

/// <summary>Chance that an offer comes as an outbound + return package, and the bonus share on both payouts when both are flown.</summary>
public sealed record ReturnRule(double Chance, decimal Bonus = 0.1m);

public sealed record CargoRule(double MinKg, double MaxKg, IReadOnlyList<string> Kinds);

public sealed record DistanceRule(double MinNm, double MaxNm);

/// <summary>Relative pick weight per airport size (1 = neutral).</summary>
public sealed record SizeWeights
{
    public double Large { get; init; } = 1;
    public double Medium { get; init; } = 1;
    public double Small { get; init; } = 1;
    public double Heliport { get; init; } = 1;
    public double Seaplane { get; init; } = 1;

    public double For(AirportSize size) => Math.Max(0, size switch
    {
        AirportSize.Large => Large,
        AirportSize.Medium => Medium,
        AirportSize.Small => Small,
        AirportSize.Heliport => Heliport,
        _ => Seaplane
    });
}

/// <summary>
/// Which destinations a mission type favours. Real airport data is dominated by small fields, so airline-style
/// missions weight big airports up and can demand a minimum share of large destinations per market search.
/// </summary>
public sealed record DestinationRule
{
    /// <summary>Allowed destination sizes; empty = all.</summary>
    public IReadOnlyList<AirportSize> Sizes { get; init; } = [];
    public SizeWeights SizeWeights { get; init; } = new();
    /// <summary>Minimum share (0–1) of large-airport destinations among this type's offers in one market search.</summary>
    public double MinLargeShare { get; init; }
    /// <summary>
    /// How strongly airports of big cities and capitals are preferred: the weight is multiplied by
    /// 1 + CityWeight × <see cref="CityImportance"/>. 0 = neutral.
    /// </summary>
    public double CityWeight { get; init; }
    /// <summary>Minimum share (0–1) of capital-city destinations among this type's offers in one market search.</summary>
    public double MinCapitalShare { get; init; }
    /// <summary>
    /// Exponent on the distance weighting (0–1, default 1). Below 1 the preferred trip length matters less,
    /// so big cities and capitals win over closer-fitting but unimportant airports.
    /// </summary>
    public double DistanceInfluence { get; init; } = 1;

    public bool Allows(AirportSize size) => Sizes.Count == 0 || Sizes.Contains(size);

    public double WeightFor(Airport airport) => SizeWeights.For(airport.Size) * (1 + Math.Max(0, CityWeight) * CityImportance(airport));

    /// <summary>
    /// 0 for towns under 100 000 inhabitants, 1 for a million, 2 for ten million, plus 1 for a capital.
    /// Small fields without scheduled service score 0: they sit near cities without serving them.
    /// </summary>
    /// <summary>A capital's airport that actually serves it (not a sports field on the outskirts).</summary>
    public static bool ServesCapital(Airport airport) =>
        airport.IsCapital && (airport.Size is AirportSize.Large or AirportSize.Medium || airport.ScheduledService);

    public static double CityImportance(Airport airport)
    {
        if (airport.Size is not (AirportSize.Large or AirportSize.Medium) && !airport.ScheduledService) return 0;
        double size = airport.CityPopulation <= 100_000 ? 0 : Math.Min(2, Math.Log10(airport.CityPopulation / 100_000.0));
        return size + (airport.IsCapital ? 1 : 0);
    }
}

public sealed record PayRule
{
    public decimal Base { get; init; }
    public decimal PerNm { get; init; }
    public decimal PerPax { get; init; }
    public decimal PerCargoKg { get; init; }
    /// <summary>Ticket share per seated passenger and nautical mile.</summary>
    public decimal PerPaxNm { get; init; }
    /// <summary>
    /// Share (0–1) of the hourly-rate part that depends on seat occupancy: 0.5 pays a half-full cabin 75 % of it.
    /// </summary>
    public decimal OccupancyShare { get; init; }
    /// <summary>
    /// Share of the aircraft's market hourly rate (<see cref="FleetEconomics.HourlyRate"/>) paid per planned block
    /// hour. Real charter prices follow the aircraft class, so a Citation X earns more than a Caravan on the same trip.
    /// </summary>
    public decimal HourlyRateFactor { get; init; }
    /// <summary>Bonus share (e.g. 0.2 = +20 %) for an on-time arrival.</summary>
    public decimal OnTimeBonus { get; init; }
}

/// <summary>Translated texts of a mission type.</summary>
public sealed record MissionTypeText(string? Name = null, string? Description = null);

/// <summary>Scoring weights; a VIP flight cares far more about a smooth landing than a cargo run.</summary>
public sealed record ScoringRule
{
    public double LandingRateWeight { get; init; } = 1;
    public double GForceWeight { get; init; } = 1;
    public double PunctualityWeight { get; init; } = 1;
    /// <summary>Max acceptable touchdown rate in ft/min (positive number) before penalties.</summary>
    public double MaxTouchdownFpm { get; init; } = 600;
    public double MaxGForce { get; init; } = 1.8;
}

/// <summary>
/// Limits a sensitive load sets on how the aircraft is flown (a patient, live animals, dangerous goods). Each limit that
/// was exceeded for more than a moment (see <see cref="Domain.Career.ComfortStats"/>) costs <see cref="Penalty"/> of the
/// payout, at most <see cref="MaxBreaches"/> times.
/// </summary>
public sealed record HandlingRule
{
    public const int MaxBreaches = 2;

    public double? MaxGForce { get; init; }
    public double? MaxBankDeg { get; init; }
    /// <summary>Highest sink rate in ft/min (positive number).</summary>
    public double? MaxSinkFpm { get; init; }
    /// <summary>Payout share per exceeded limit.</summary>
    public decimal Penalty { get; init; } = 0.10m;
    /// <summary>Why: "patient", "livestock", "dangerous" or "fragile" (the default).</summary>
    public string Reason { get; init; } = "fragile";

    public bool HasLimits => MaxGForce is not null || MaxBankDeg is not null || MaxSinkFpm is not null;

    public string ReasonText => Reason switch
    {
        "patient" => L.T("Patient on board"),
        "livestock" => L.T("Live animals"),
        "dangerous" => L.T("Dangerous goods"),
        _ => L.T("Fragile load")
    };

    /// <summary>"max 1.3 G · bank 25° · sink 1,500 fpm".</summary>
    public string LimitsText => string.Join(" · ", new[]
    {
        MaxGForce is { } g ? L.F("max {0:0.0} G", g) : null,
        MaxBankDeg is { } b ? L.F("bank {0:N0}°", b) : null,
        MaxSinkFpm is { } s ? L.F("sink {0:N0} fpm", s) : null
    }.Where(x => x is not null));

    /// <summary>The limits the flight exceeded, worded for the settlement ("bank 34° (max 25°)").</summary>
    public IReadOnlyList<string> Breaches(Domain.Career.ComfortStats stats)
    {
        var breaches = new List<string>();
        if (MaxGForce is { } g && stats.SustainedG > g) breaches.Add(L.F("{0:0.00} G (max {1:0.0} G)", stats.SustainedG, g));
        if (MaxBankDeg is { } b && stats.SustainedBankDeg > b) breaches.Add(L.F("bank {0:N0}° (max {1:N0}°)", stats.SustainedBankDeg, b));
        if (MaxSinkFpm is { } s && stats.SustainedSinkFpm > s) breaches.Add(L.F("sink rate {0:N0} fpm (max {1:N0} fpm)", stats.SustainedSinkFpm, s));
        return breaches;
    }
}

/// <summary>
/// How tight the job's deadline is and what arriving early is worth. The deadline is the planned block time times
/// <see cref="DeadlineFactor"/> plus <see cref="DeadlineSlackHours"/> after the offer was made.
/// </summary>
public sealed record TimingRule
{
    public double DeadlineFactor { get; init; } = 1.5;
    public double DeadlineSlackHours { get; init; } = 2;
    /// <summary>Payout share per minute the flight arrives before its deadline (0 = no early bonus).</summary>
    public decimal PerMinuteEarly { get; init; }
    /// <summary>Highest early bonus as a share of the payout.</summary>
    public decimal MaxEarlyBonus { get; init; } = 0.15m;

    /// <summary>Early bonus as a share of the payout for arriving <paramref name="early"/> before the deadline.</summary>
    public decimal EarlyBonus(TimeSpan early) =>
        PerMinuteEarly <= 0 || early <= TimeSpan.Zero ? 0 : Math.Min(MaxEarlyBonus, PerMinuteEarly * (decimal)Math.Floor(early.TotalMinutes));
}

/// <summary>
/// A mission type as defined in content/missions/*.json. New types are added by dropping a JSON file;
/// only types with new generation logic need a new <see cref="IMissionGenerator"/>.
/// </summary>
public sealed record MissionType
{
    public required string Id { get; init; }
    /// <summary>English name (the default language); translations sit in <see cref="Texts"/>.</summary>
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Translated name and description per language code, e.g. "de".</summary>
    public IReadOnlyDictionary<string, MissionTypeText> Texts { get; init; } = new Dictionary<string, MissionTypeText>();

    /// <summary>Name in the current UI language (falls back to <see cref="Name"/>).</summary>
    public string DisplayName => Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Name) ? t.Name : Name;
    public string DisplayDescription =>
        Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Description) ? t.Description : Description;
    public string Category { get; init; } = "general";
    /// <summary>Name of the generator that builds concrete offers; "standard" covers airport-to-airport work.</summary>
    public string Generator { get; init; } = "standard";
    public MissionRequirements Requirements { get; init; } = new();
    public PaxRule? Passengers { get; init; }
    public CargoRule? Cargo { get; init; }
    public DistanceRule Distance { get; init; } = new(20, 500);
    public DestinationRule Destinations { get; init; } = new();
    public PayRule Pay { get; init; } = new();
    /// <summary>Offers may come with a return flight (null = never).</summary>
    public ReturnRule? ReturnFlight { get; init; }
    public ScoringRule Scoring { get; init; } = new();
    /// <summary>Limits on how the load may be flown (null = none).</summary>
    public HandlingRule? Handling { get; init; }
    public TimingRule Timing { get; init; } = new();
    public SimBriefUsage SimBrief { get; init; } = SimBriefUsage.Optional;
    public FlightRulesRequirement FlightRules { get; init; } = FlightRulesRequirement.Any;
    /// <summary>Hours the offer stays on the market before expiring.</summary>
    public double OfferValidityHours { get; init; } = 48;
    /// <summary>Relative frequency on the mission market.</summary>
    public double Weight { get; init; } = 1;
    public int MinReputation { get; init; }
}
