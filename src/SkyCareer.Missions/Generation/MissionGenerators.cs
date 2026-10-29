using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;

namespace SkyCareer.Missions.Generation;

public sealed record MissionOffer
{
    public required Guid Id { get; init; }
    public required MissionType Type { get; init; }
    public required AircraftProfile Aircraft { get; init; }
    public required Airport Origin { get; init; }
    public required Airport Destination { get; init; }
    public required double DistanceNm { get; init; }
    public PassengerCount Passengers { get; init; } = new(0);
    public Mass Cargo { get; init; } = Mass.Zero;
    public string CargoKind { get; init; } = "";
    public required DateTimeOffset Deadline { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required Money Payout { get; init; }
    public required LoadSheet PlannedLoad { get; init; }
}

public sealed record GenerationContext(
    MissionType Type,
    AircraftProfile Aircraft,
    Airport Origin,
    IReadOnlyList<Airport> Airports,
    DateTimeOffset GameTime,
    Random Rng)
{
    /// <summary>Optional job search the offer has to satisfy (distance, load, destination, payout).</summary>
    public MissionSearch Search { get; init; } = MissionSearch.Any;

    /// <summary>Only large airports may be the destination (used to keep a minimum share of big airports).</summary>
    public bool RequireLarge { get; init; }
    /// <summary>Only capital-city airports (<see cref="DestinationRule.ServesCapital"/>); used to keep a type's minimum capital share.</summary>
    public bool RequireCapital { get; init; }
    /// <summary>Passenger and baggage masses of the career (adult weight from the settings).</summary>
    public PassengerMassTable Masses { get; init; } = PassengerMassTable.Default;

    /// <summary>
    /// Destinations already used in the current search. Picking one of them again on purpose produces several jobs
    /// to the same hub, as real markets have.
    /// </summary>
    public IReadOnlySet<string> RecentDestinations { get; init; } = new HashSet<string>();
}

/// <summary>Builds concrete offers for one mission type. Register new implementations by <see cref="Name"/>.</summary>
public interface IMissionGenerator
{
    string Name { get; }
    MissionOffer? TryGenerate(GenerationContext ctx);
}

/// <summary>
/// Airport-to-airport missions (scheduled, cargo, charter, air ambulance, bush supply …). Picks a reachable
/// destination the aircraft can use, sizes the load from the mission rules and rejects offers whose load sheet
/// would break aircraft limits, so the player never gets an impossible job.
/// Destinations are weighted around a preferred distance that grows with the aircraft's range, so long-range
/// aircraft mostly get long flights while short hops stay possible.
/// </summary>
public sealed class StandardMissionGenerator : IMissionGenerator
{
    /// <summary>Preferred trip length as a share of the aircraft's still-air range.</summary>
    public const double PreferredRangeShare = 0.45;
    /// <summary>Spread of the log-normal distance weighting (larger = more variety).</summary>
    private const double Sigma = 0.6;
    /// <summary>Floor weight so destinations far from the preferred distance still occur occasionally.</summary>
    private const double MinWeight = 0.05;
    /// <summary>Chance to reuse one of the search's recent destinations when one fits.</summary>
    public const double RepeatChance = 0.3;

    public string Name => "standard";

    public MissionOffer? TryGenerate(GenerationContext ctx)
    {
        var (type, aircraft, origin, airports, now, rng) = ctx;
        var search = ctx.Search;
        double range = RequirementEvaluator.EstimatedRangeNm(aircraft);

        double minNm = Math.Max(type.Distance.MinNm, search.DistanceMinNm ?? 0);
        double maxNm = Math.Min(Math.Min(type.Distance.MaxNm, search.DistanceMaxNm ?? double.MaxValue), range * 0.9);
        if (minNm > maxNm) return null;

        var rule = type.Destinations;
        var candidates = airports
            .Where(a => a.Ident != origin.Ident && IsUsable(a, aircraft) && search.AcceptsDestination(a) && rule.Allows(a.Size)
                        && (!ctx.RequireLarge || a.Size == AirportSize.Large) && (!ctx.RequireCapital || DestinationRule.ServesCapital(a)))
            .Select(a => (Airport: a, Dist: origin.Position.DistanceNmTo(a.Position)))
            .Where(x => x.Dist >= minNm && x.Dist <= maxNm)
            .ToList();
        if (candidates.Count == 0) return null;

        // Sometimes stay with a destination this search already used, so hubs collect several jobs.
        if (ctx.RecentDestinations.Count > 0 && rng.NextDouble() < RepeatChance)
        {
            var repeats = candidates.Where(c => ctx.RecentDestinations.Contains(c.Airport.Ident)).ToList();
            if (repeats.Count > 0) candidates = repeats;
        }

        double preferred = PreferredDistance(range, minNm, maxNm);
        var (dest, dist) = PickWeighted(candidates,
            c => Math.Pow(DistanceWeight(c.Dist, preferred), Math.Clamp(rule.DistanceInfluence, 0, 1))
                 * DensityCompensation(c.Dist, preferred) * rule.WeightFor(c.Airport), rng);

        // Load limits from the mission rule narrowed by the search.
        int paxMin = Math.Max(type.Passengers?.Min ?? 0, search.PaxMin ?? 0);
        int paxMax = Math.Min(Math.Min(type.Passengers?.Max ?? 0, aircraft.PassengerSeats), search.PaxMax ?? int.MaxValue);
        double cargoMin = Math.Max(type.Cargo?.MinKg ?? 0, search.CargoMinKg ?? 0);
        if (paxMin > paxMax || (type.Cargo is null && cargoMin > 0) || (type.Cargo is { } cr && cargoMin > cr.MaxKg)) return null;

        var pax = DrawPassengers(type.Passengers, paxMin, paxMax, rng);
        var (cargo, kind) = DrawCargo(type.Cargo, cargoMin, rng);
        var rules = type.FlightRules == FlightRulesRequirement.Vfr ? FlightRules.Vfr : FlightRules.Ifr;
        var fuel = FuelPlanner.Plan(aircraft, dist, rules, rules == FlightRules.Ifr ? Math.Min(100, dist * 0.3) : 0);
        var baggage = type.Passengers?.Baggage ?? BaggageProfile.None;
        // Every passenger's bag weighs differently; SimBrief gets the sum as freight.
        var sheet = LoadSheetBuilder.Build(aircraft, pax, baggage, cargo, fuel, table: ctx.Masses, rng: rng, baggageMode: PassengerMassMode.Randomized);

        // Trim cargo first, then passengers, until the load fits; give up if the minimums can't be kept.
        for (int i = 0; i < 10 && !sheet.IsWithinLimits; i++)
        {
            if (cargo.Kg > cargoMin) cargo = Mass.FromKg(Math.Max(cargoMin, cargo.Kg * 0.8));
            else if (pax.Seated > paxMin) pax = pax with { Adults = Math.Max(paxMin - pax.Children, (int)(pax.Adults * 0.85)) };
            else break;
            sheet = LoadSheetBuilder.Build(aircraft, pax, baggage, cargo, fuel, table: ctx.Masses, rng: rng, baggageMode: PassengerMassMode.Randomized);
        }
        if (!sheet.IsWithinLimits || pax.Seated < paxMin || cargo.Kg < cargoMin) return null;

        double blockHours = dist / aircraft.CruiseKtas + 0.5;
        var pay = type.Pay;
        var payout = new Money(Math.Round(pay.Base + pay.PerNm * (decimal)dist + pay.PerPax * pax.Total + pay.PerCargoKg * (decimal)cargo.Kg
                                          + pay.HourlyRateFactor * FleetEconomics.HourlyRate(aircraft) * (decimal)blockHours, 0));
        if (search.PayoutMin is { } minPay && payout.Amount < minPay) return null;

        return new MissionOffer
        {
            Id = Guid.NewGuid(),
            Type = type,
            Aircraft = aircraft,
            Origin = origin,
            Destination = dest,
            DistanceNm = dist,
            Passengers = pax,
            Cargo = cargo,
            CargoKind = kind,
            Deadline = now.AddHours(blockHours * 1.5 + 2),
            ExpiresAt = now.AddHours(type.OfferValidityHours),
            Payout = payout,
            PlannedLoad = sheet
        };
    }

    /// <summary>Distance the generator aims for: a share of the range, kept inside the allowed window.</summary>
    public static double PreferredDistance(double rangeNm, double minNm, double maxNm) =>
        Math.Clamp(rangeNm * PreferredRangeShare, minNm, Math.Max(minNm, maxNm));

    /// <summary>
    /// The number of airports at distance d grows roughly with d (ring area), so far rings would win by sheer count.
    /// Dividing by d^1.5 (slightly more than the ring growth) lets the distance curve, not the airport count, decide
    /// the trip length and keeps medium trips common for long-range aircraft (Citation X charter: ~half at 400–1200 nm).
    /// </summary>
    public static double DensityCompensation(double distanceNm, double preferredNm) =>
        Math.Pow(Math.Max(1, preferredNm) / Math.Max(25, distanceNm), 1.5);

    /// <summary>Log-normal weight around the preferred distance plus a floor.</summary>
    public static double DistanceWeight(double distanceNm, double preferredNm)
    {
        double x = Math.Log(Math.Max(1, distanceNm) / Math.Max(1, preferredNm));
        return MinWeight + Math.Exp(-x * x / (2 * Sigma * Sigma));
    }

    private static T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weight, Random rng)
    {
        double total = items.Sum(weight);
        double roll = rng.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }
        return items[^1];
    }

    private static bool IsUsable(Airport a, AircraftProfile aircraft) =>
        a.LongestRunwayM >= aircraft.MinRunwayM && a.Surfaces.Any(aircraft.Surfaces.Contains) && (!a.IsMilitary || aircraft.IsMilitary);

    private static PassengerCount DrawPassengers(PaxRule? rule, int min, int max, Random rng)
    {
        if (rule is null || max <= 0) return new PassengerCount(0);
        int seated = rng.Next(min, max + 1);
        int children = Math.Min(seated, (int)Math.Round(seated * rule.ChildShare * rng.NextDouble() * 2));
        int infants = (int)Math.Round(seated * rule.InfantShare * rng.NextDouble() * 2);
        return new PassengerCount(seated - children, children, infants);
    }

    private static (Mass, string) DrawCargo(CargoRule? rule, double minKg, Random rng)
    {
        if (rule is null) return (Mass.Zero, "");
        double kg = minKg + rng.NextDouble() * (rule.MaxKg - minKg);
        string kind = rule.Kinds.Count > 0 ? rule.Kinds[rng.Next(rule.Kinds.Count)] : "general";
        return (Mass.FromKg(Math.Ceiling(kg / 5) * 5), kind);
    }
}

public sealed class MissionGeneratorRegistry(IEnumerable<IMissionGenerator> generators)
{
    private readonly Dictionary<string, IMissionGenerator> _byName =
        generators.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

    public IMissionGenerator Get(MissionType type) =>
        _byName.TryGetValue(type.Generator, out var g)
            ? g
            : throw new InvalidOperationException($"Mission type '{type.Id}' uses unknown generator '{type.Generator}'.");
}
