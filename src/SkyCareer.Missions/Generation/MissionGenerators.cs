using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
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
    /// <summary>Demand event at the destination that raised the payout; null for none.</summary>
    public DemandEvent? Demand { get; init; }
    /// <summary>The later legs when the offer comes as a package, in flying order; each starts where the one before ends.</summary>
    public IReadOnlyList<MissionOffer> FollowUps { get; init; } = [];
    public PackageKind PackageKind { get; init; } = PackageKind.Return;
    /// <summary>Bonus share on all payouts of the package once every leg is flown.</summary>
    public decimal PackageBonus { get; init; }
    /// <summary>The way back when the offer is a round trip.</summary>
    public MissionOffer? Return => PackageKind == PackageKind.Return ? FollowUps.FirstOrDefault() : null;
    /// <summary>Waypoints of a round trip, in flying order; empty for ordinary jobs.</summary>
    public IReadOnlyList<TourWaypoint> Waypoints { get; init; } = [];
    /// <summary>All legs of the offer, this one first.</summary>
    public IEnumerable<MissionOffer> Legs => FollowUps.Prepend(this);
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
    /// Bags weigh differently per passenger (the sum reaches SimBrief as freight). Off: one standard bag per passenger,
    /// as SimBrief adds them itself.
    /// </summary>
    public bool VaryBaggage { get; init; }
    /// <summary>The career's reputation (0–100); raises the pay by <see cref="StandardMissionGenerator.ReputationFactor"/>.</summary>
    public int Reputation { get; init; }
    /// <summary>
    /// Destinations that already have as many open offers of this type as the market allows
    /// (<c>CareerService.MaxOffersPerDestination</c>); they are not picked again. Null = no limit.
    /// </summary>
    public Func<Airport, bool>? DestinationFull { get; init; }
    /// <summary>Destinations without any open offer yet; they are picked more often to spread the market. Null = no preference.</summary>
    public Func<Airport, bool>? Unserved { get; init; }

    /// <summary>Weight factor for a destination without any open offer.</summary>
    public const double UnservedWeight = 2;

    /// <summary>This week's demand event at an airport (<see cref="DemandEvents.At"/>); null = no events.</summary>
    public Func<Airport, DemandEvent?>? Demand { get; init; }

    /// <summary>The event at <paramref name="airport"/> if it concerns this mission type.</summary>
    public DemandEvent? DemandAt(Airport airport) => Demand?.Invoke(airport) is { } e && e.AppliesTo(Type) ? e : null;

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
    /// <summary>Pay bonus at reputation 100; it grows linearly from 0 at reputation 0 (+5 % at the start value 10).</summary>
    public const decimal MaxReputationBonus = 0.5m;

    /// <summary>Pay multiplier for a reputation (0–100): a better name earns better rates.</summary>
    public static decimal ReputationFactor(int reputation) => 1 + MaxReputationBonus * Math.Clamp(reputation, 0, 100) / 100m;
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
                        && (!ctx.RequireLarge || a.Size == AirportSize.Large) && (!ctx.RequireCapital || DestinationRule.ServesCapital(a))
                        && ctx.DestinationFull?.Invoke(a) != true)
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
                 * DensityCompensation(c.Dist, preferred) * rule.WeightFor(c.Airport)
                 * (ctx.DemandAt(c.Airport) is null ? 1 : DemandEvents.DestinationWeight)
                 * (ctx.Unserved?.Invoke(c.Airport) == true ? GenerationContext.UnservedWeight : 1), rng);

        // A multi-stop run picks its first stop by leg length, not by the type's (longer) trip distance.
        if (type.MultiStop is { } multi && rng.NextDouble() < multi.Chance && TryMultiStop(ctx, multi) is { } run) return run;

        var outbound = TryBuild(ctx, origin, dest, dist, now, search);
        if (outbound is null) return null;
        if (type.ReturnFlight is not { } ret || rng.NextDouble() >= ret.Chance) return outbound;

        // The way back starts after the outbound's deadline plus a turnaround; the search only shaped the outbound.
        var back = TryBuild(ctx, dest, origin, dist, outbound.Deadline.AddMinutes(30), MissionSearch.Any);
        return back is null ? outbound
            : outbound with { FollowUps = [back with { ExpiresAt = outbound.ExpiresAt }], PackageKind = PackageKind.Return, PackageBonus = ret.Bonus };
    }

    /// <summary>Turnaround between two legs of a package.</summary>
    public static readonly TimeSpan LegTurnaround = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A run over <see cref="MultiStopRule.MinLegs"/>–<see cref="MultiStopRule.MaxLegs"/> legs, each to a stop within the
    /// rule's leg length that the run hasn't visited yet (and back to the origin with <see cref="MultiStopRule.ReturnToOrigin"/>).
    /// Null when no such chain fits.
    /// </summary>
    private static MissionOffer? TryMultiStop(GenerationContext ctx, MultiStopRule rule)
    {
        var (type, aircraft, origin, airports, now, rng) = ctx;
        double range = RequirementEvaluator.EstimatedRangeNm(aircraft);
        int legs = rng.Next(Math.Clamp(rule.MinLegs, 2, 4), Math.Clamp(rule.MaxLegs, 2, 4) + 1);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { origin.Ident };
        var built = new List<MissionOffer>();
        var from = origin;
        var start = now;
        for (int leg = 1; leg <= legs; leg++)
        {
            bool home = rule.ReturnToOrigin && leg == legs;
            Airport? to;
            double dist;
            if (home)
            {
                (to, dist) = (origin, from.Position.DistanceNmTo(origin.Position));
                if (dist > Math.Min(rule.MaxLegNm * 1.5, range * 0.9)) return null;
            }
            else
            {
                var stops = airports
                    .Where(a => !visited.Contains(a.Ident) && IsUsable(a, aircraft) && type.Destinations.Allows(a.Size)
                                // The first stop is the offer's destination: it honours the search and the large/capital shares.
                                && (leg > 1 || ctx.Search.AcceptsDestination(a) && (!ctx.RequireLarge || a.Size == AirportSize.Large)
                                    && (!ctx.RequireCapital || DestinationRule.ServesCapital(a)) && ctx.DestinationFull?.Invoke(a) != true))
                    .Select(a => (Airport: a, Dist: from.Position.DistanceNmTo(a.Position)))
                    .Where(x => x.Dist >= rule.MinLegNm && x.Dist <= Math.Min(rule.MaxLegNm, range * 0.9))
                    // The last outward stop of a run that returns must still be within reach of home.
                    .Where(x => !(rule.ReturnToOrigin && leg == legs - 1) || x.Airport.Position.DistanceNmTo(origin.Position) <= rule.MaxLegNm * 1.5)
                    .ToList();
                if (stops.Count == 0) return null;
                (to, dist) = PickWeighted(stops, s => type.Destinations.WeightFor(s.Airport) * (ctx.DemandAt(s.Airport) is null ? 1 : DemandEvents.DestinationWeight)
                                                      * (leg == 1 && ctx.Unserved?.Invoke(s.Airport) == true ? GenerationContext.UnservedWeight : 1), rng);
            }
            var offer = TryBuild(ctx, from, to, dist, start, leg == 1 ? ctx.Search : MissionSearch.Any, rule.SameLoad && leg > 1 ? built[0] : null);
            if (offer is null) return null;
            built.Add(leg == 1 ? offer : offer with { ExpiresAt = built[0].ExpiresAt });
            visited.Add(to.Ident);
            from = to;
            start = offer.Deadline + LegTurnaround;
        }
        return built[0] with { FollowUps = built.Skip(1).ToList(), PackageKind = PackageKind.MultiStop, PackageBonus = rule.Bonus };
    }

    /// <summary>One leg sized, trimmed and priced like the standard generator's; for other generators (round trips).</summary>
    public static MissionOffer? BuildLeg(GenerationContext ctx, Airport from, Airport to, double dist, DateTimeOffset start, MissionSearch search) =>
        TryBuild(ctx, from, to, dist, start, search);

    /// <summary>
    /// Sizes the load for one leg, trims it to the aircraft's limits and prices it; null if the rules can't be met. With
    /// <paramref name="sameAs"/> the leg carries exactly that leg's load (the same group on a roadshow): it is never trimmed,
    /// and a leg on which it doesn't fit (more fuel on a longer leg) gives null.
    /// </summary>
    private static MissionOffer? TryBuild(GenerationContext ctx, Airport from, Airport to, double dist, DateTimeOffset start, MissionSearch search,
        MissionOffer? sameAs = null)
    {
        var (type, aircraft, _, _, now, rng) = ctx;
        if (sameAs is not null) return BuildWithLoad(ctx, from, to, dist, start, sameAs);

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
        var bagMode = ctx.VaryBaggage ? PassengerMassMode.Randomized : PassengerMassMode.Standard;
        var sheet = LoadSheetBuilder.Build(aircraft, pax, baggage, cargo, fuel, table: ctx.Masses, rng: rng, baggageMode: bagMode);

        // Trim cargo first, then passengers, until the load fits; give up if the minimums can't be kept.
        for (int i = 0; i < 10 && !sheet.IsWithinLimits; i++)
        {
            if (cargo.Kg > cargoMin) cargo = Mass.FromKg(Math.Max(cargoMin, cargo.Kg * 0.8));
            else if (pax.Seated > paxMin) pax = pax with { Adults = Math.Max(paxMin - pax.Children, (int)(pax.Adults * 0.85)) };
            else break;
            sheet = LoadSheetBuilder.Build(aircraft, pax, baggage, cargo, fuel, table: ctx.Masses, rng: rng, baggageMode: bagMode);
        }
        if (!sheet.IsWithinLimits || pax.Seated < paxMin || cargo.Kg < cargoMin) return null;

        var demand = ctx.DemandAt(to);
        var payout = new Money(Math.Round(Payout(type, aircraft, dist, pax, cargo).Amount * ReputationFactor(ctx.Reputation) * (demand?.PayFactor ?? 1), 0));
        if (search.PayoutMin is { } minPay && payout.Amount < minPay) return null;
        if (!search.AcceptsPayPerHour(payout.Amount, BlockHours(aircraft, dist))) return null;

        return new MissionOffer
        {
            Id = Guid.NewGuid(),
            Type = type,
            Aircraft = aircraft,
            Origin = from,
            Destination = to,
            DistanceNm = dist,
            Passengers = pax,
            Cargo = cargo,
            CargoKind = kind,
            Deadline = start.AddHours(BlockHours(aircraft, dist) * type.Timing.DeadlineFactor + type.Timing.DeadlineSlackHours),
            ExpiresAt = now.AddHours(type.OfferValidityHours),
            Payout = payout,
            Demand = demand,
            PlannedLoad = sheet
        };
    }

    /// <summary>A leg carrying another leg's frozen load (passengers, bags, cargo) unchanged; null when it exceeds a limit.</summary>
    private static MissionOffer? BuildWithLoad(GenerationContext ctx, Airport from, Airport to, double dist, DateTimeOffset start, MissionOffer load)
    {
        var (type, aircraft, _, _, now, _) = ctx;
        var rules = type.FlightRules == FlightRulesRequirement.Vfr ? FlightRules.Vfr : FlightRules.Ifr;
        var fuel = FuelPlanner.Plan(aircraft, dist, rules, rules == FlightRules.Ifr ? Math.Min(100, dist * 0.3) : 0);
        var sheet = LoadSheetBuilder.FromMasses(aircraft, load.Passengers, load.PlannedLoad.PassengerMass, load.PlannedLoad.BaggageMass, load.Cargo, fuel);
        if (!sheet.IsWithinLimits) return null;
        var demand = ctx.DemandAt(to);
        var payout = new Money(Math.Round(Payout(type, aircraft, dist, load.Passengers, load.Cargo).Amount * ReputationFactor(ctx.Reputation) * (demand?.PayFactor ?? 1), 0));
        return load with
        {
            Id = Guid.NewGuid(), Origin = from, Destination = to, DistanceNm = dist,
            Deadline = start.AddHours(BlockHours(aircraft, dist) * type.Timing.DeadlineFactor + type.Timing.DeadlineSlackHours),
            ExpiresAt = now.AddHours(type.OfferValidityHours), Payout = payout, Demand = demand, PlannedLoad = sheet,
            FollowUps = [], Waypoints = []
        };
    }

    public static double BlockHours(AircraftProfile aircraft, double distanceNm) => distanceNm / aircraft.CruiseKtas + 0.5;

    /// <summary>Seats the mission can fill on this aircraft (the type's maximum may be lower than the cabin).</summary>
    public static int Capacity(MissionType type, AircraftProfile aircraft) =>
        type.Passengers is { } p ? Math.Min(p.Max, aircraft.PassengerSeats) : 0;

    /// <summary>
    /// Mission pay: fixed and distance terms, per passenger (plus a ticket share per passenger and nm), per cargo kg and a
    /// share of the aircraft's hourly rate per block hour. With <see cref="PayRule.OccupancyShare"/> part of the hourly
    /// share follows how full the cabin is, so a half-empty flight pays noticeably less.
    /// </summary>
    public static Money Payout(MissionType type, AircraftProfile aircraft, double dist, PassengerCount pax, Mass cargo)
    {
        var pay = type.Pay;
        int capacity = Capacity(type, aircraft);
        decimal occupancy = capacity > 0 ? (decimal)Math.Min(1, pax.Seated / (double)capacity) : 1;
        decimal hourly = pay.HourlyRateFactor * FleetEconomics.HourlyRate(aircraft) * (decimal)BlockHours(aircraft, dist)
                         * (1 - pay.OccupancyShare + pay.OccupancyShare * occupancy);
        return new Money(Math.Round(pay.Base + pay.PerNm * (decimal)dist + pay.PerPax * pax.Total + pay.PerPaxNm * pax.Seated * (decimal)dist
                                    + pay.PerCargoKg * (decimal)cargo.Kg + hourly, 0));
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
        int seated = rule.LoadFactor is { } lf
            ? Math.Clamp((int)Math.Round(max * Math.Clamp(lf + Gaussian(rng) * rule.LoadFactorSpread, Math.Max(0, lf - 2 * rule.LoadFactorSpread), 1)), min, max)
            : rng.Next(min, max + 1);
        int children = Math.Min(seated, (int)Math.Round(seated * rule.ChildShare * rng.NextDouble() * 2));
        int infants = (int)Math.Round(seated * rule.InfantShare * rng.NextDouble() * 2);
        return new PassengerCount(seated - children, children, infants);
    }

    /// <summary>Standard normal sample (Box–Muller).</summary>
    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

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
