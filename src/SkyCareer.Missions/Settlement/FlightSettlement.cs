using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Settlement;

public sealed record SettlementInput(
    MissionType Type,
    MissionContract Contract,
    AircraftProfile Profile,
    FleetAircraft Aircraft,
    Airport Origin,
    Airport Destination,
    Airport? ActualArrival, // airport nearest to where the aircraft ended up (diversions); null if unknown
    FlightResult Result,
    DateTime DepartureGameTime,
    EconomyTables Economy,
    bool ExternalGroundCosts = false, // fuel, handling and catering are paid from GSX receipts instead
    double? FuelUpliftKg = null,      // fuel stays in the aircraft: pay what was added since the last flight, not what was burnt
    decimal PackageBonus = 0);        // return leg of a package whose outbound was flown: bonus on both payouts

public sealed record SettlementResult(
    FlightOutcome Outcome,
    FlightScore Score,
    IReadOnlyList<CostLine> Lines,
    string FinalLocation,
    DateTime ArrivalGameTime,
    double BlockHours,
    double AirHours,
    double ConditionLoss,
    int ReputationChange,
    IReadOnlyList<string> Notes)
{
    public decimal Revenue => Lines.Where(l => l.Amount > 0).Sum(l => l.Amount);
    public decimal Costs => -Lines.Where(l => l.Amount < 0).Sum(l => l.Amount);
    public decimal Profit => Lines.Sum(l => l.Amount);
}

/// <summary>
/// Turns a finished flight into money, reputation and wear. Pure function: the caller persists the result.
/// Revenue lines are positive, cost lines negative.
/// </summary>
public static class FlightSettlement
{
    /// <summary>Aircraft must stop within this distance of the destination's reference point.</summary>
    public const double ArrivalRadiusNm = 3;
    /// <summary>Payout share deducted per departure check that was red at liftoff, capped at three.</summary>
    public const decimal DepartureIssuePenalty = 0.10m;
    /// <summary>All rule violations of one flight together never cost more than this share of the payout.</summary>
    public const decimal MaxViolationPenalty = 0.20m;
    /// <summary>Reputation per GSX service completed before departure on a passenger flight, at most <see cref="MaxGroundServiceReputation"/>.</summary>
    public const int GroundServiceReputation = 1;
    public const int MaxGroundServiceReputation = 2;

    /// <summary>Reputation the GSX services before departure earn: only with passengers aboard.</summary>
    public static int GroundServiceBonus(MissionContract c) =>
        c.TotalPassengers > 0 ? Math.Min(MaxGroundServiceReputation, c.GroundServiceList.Count * GroundServiceReputation) : 0;

    public static string GroundServiceText(GroundService service) => service switch
    {
        GroundService.Catering => L.T("Catering"),
        GroundService.Cleaning => L.T("Cabin cleaning"),
        GroundService.Water => L.T("Potable water"),
        _ => L.T("Lavatory service")
    };

    /// <summary>Payout share deducted once per kind of rule violation; time acceleration is only noted.</summary>
    public static decimal ViolationPenalty(ViolationType type) => type switch
    {
        ViolationType.Slew or ViolationType.Teleport or ViolationType.AircraftChanged => 0.20m,
        ViolationType.InflightRefuel => 0.15m,
        ViolationType.PayloadChanged => 0.10m,
        _ => 0m
    };

    public static string ViolationText(ViolationType type) => type switch
    {
        ViolationType.Slew => L.T("Slew mode"),
        ViolationType.SimRate => L.T("Time acceleration"),
        ViolationType.InflightRefuel => L.T("In-flight refuelling"),
        ViolationType.PayloadChanged => L.T("Payload changed in flight"),
        ViolationType.Teleport => L.T("Position jump"),
        _ => L.T("Aircraft change")
    };

    /// <summary>The penalised kinds of a flight with their (capped) payout shares, in order of severity.</summary>
    public static IReadOnlyList<(ViolationType Type, decimal Share)> ViolationPenalties(IEnumerable<RuleViolation> violations)
    {
        var result = new List<(ViolationType, decimal)>();
        decimal left = MaxViolationPenalty;
        foreach (var type in violations.Select(v => v.Type).Distinct().Where(k => ViolationPenalty(k) > 0).OrderByDescending(ViolationPenalty))
        {
            decimal share = Math.Min(ViolationPenalty(type), left);
            left -= share;
            result.Add((type, share));
        }
        return result;
    }

    public static SettlementResult Settle(SettlementInput i)
    {
        var r = i.Result;
        var c = i.Contract;
        var t = i.Economy;
        var notes = new List<string>(r.Notes);
        var lines = new List<CostLine>();

        double airHours = r.AirTime.TotalHours;
        double blockHours = Math.Max(r.BlockTime.TotalHours, airHours);
        if (blockHours <= 0) blockHours = c.DistanceNm / Math.Max(1, i.Profile.CruiseKtas) + 0.3;
        var arrivalTime = i.DepartureGameTime.AddHours(blockHours);
        var lateBy = arrivalTime - c.Deadline;

        bool atDestination = r.FinalPosition.DistanceNmTo(i.Destination.Position) <= ArrivalRadiusNm;
        var landedAt = atDestination ? i.Destination : i.ActualArrival ?? i.Origin;

        var outcome = r switch
        {
            { Crashed: true } => FlightOutcome.Crashed,
            _ when !atDestination => FlightOutcome.Diverted,
            _ when lateBy > TimeSpan.Zero => FlightOutcome.Late,
            _ => FlightOutcome.Completed
        };

        var score = outcome is FlightOutcome.Crashed
            ? new FlightScore(0, 0, 0, 0, 0)
            : FlightScorer.Score(i.Type.Scoring, r, lateBy);

        // Revenue
        int reputation;
        switch (outcome)
        {
            case FlightOutcome.Completed:
            case FlightOutcome.Late:
                lines.Add(new(LedgerCategory.MissionRevenue, c.Payout, $"{i.Type.DisplayName} {c.Origin}→{c.Destination}"));
                if (outcome == FlightOutcome.Completed && i.Type.Pay.OnTimeBonus > 0)
                    lines.Add(new(LedgerCategory.Bonus, Math.Round(c.Payout * i.Type.Pay.OnTimeBonus, 0), L.T("On-time bonus")));
                if (i.PackageBonus > 0)
                    lines.Add(new(LedgerCategory.Bonus, Math.Round(i.PackageBonus, 0), L.T("Return flight bonus")));
                if (outcome == FlightOutcome.Late)
                {
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.25m, 0), L.F("Late arrival {0:N0} min", lateBy.TotalMinutes)));
                    notes.Add(L.F("Deadline missed by {0:N0} min.", lateBy.TotalMinutes));
                }
                if (score.Total < 50)
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.2m, 0), L.F("Customer complaint (score {0})", score.Total)));
                reputation = outcome == FlightOutcome.Late ? -1 : 1 + (score.Total >= 85 ? 1 : 0) + (score.Total >= 95 ? 1 : 0);
                foreach (var issue in r.DepartureIssues.Take(3))
                {
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * DepartureIssuePenalty, 0), L.F("Departure deviation: {0}", issue)));
                    notes.Add(L.F("Not in order at liftoff: {0}", issue));
                }
                if (r.DepartureIssues.Count > 0) reputation -= 1;
                if (GroundServiceBonus(c) is > 0 and var serviceBonus)
                {
                    reputation += serviceBonus;
                    notes.Add(L.F("GSX services before departure: {0} (+{1} reputation).",
                        string.Join(", ", c.GroundServiceList.Select(GroundServiceText)), serviceBonus));
                }
                foreach (var (type, share) in ViolationPenalties(r.Violations))
                    if (share > 0)
                        lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * share, 0), L.F("Rule violation: {0}", ViolationText(type))));
                break;
            case FlightOutcome.Diverted:
                notes.Add(L.F("Did not arrive at destination {0} (final position {1}). No payment.", c.Destination, landedAt.Ident));
                reputation = -2;
                break;
            case FlightOutcome.Crashed:
                lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.2m, 0), L.T("Contract penalty after accident")));
                reputation = -10;
                break;
            default:
                reputation = 0;
                break;
        }

        // Rule violations cost part of the payout (above) and reputation, but never void the flight.
        var penalised = ViolationPenalties(r.Violations);
        if (outcome != FlightOutcome.Crashed && penalised.Count > 0)
        {
            reputation -= Math.Min(3, penalised.Count);
            notes.Add(L.F("Rule violation: {0}", string.Join(", ", penalised.Select(p => $"{ViolationText(p.Type)} (−{p.Share:P0})"))) +
                      (outcome == FlightOutcome.Diverted ? L.T(" – no payment, reputation only.") : "."));
        }
        if (r.Violations.Any(v => v.Type == ViolationType.SimRate))
            notes.Add(L.T("Time acceleration used (noted only, no deduction)."));

        // Variable costs are always due: the flight happened, whatever the outcome.
        var fuelPrice = CostModel.FuelPricePerKg(t, i.Profile.FuelType, i.Origin, i.DepartureGameTime);
        if (!i.ExternalGroundCosts && i.FuelUpliftKg is { } uplift)
            AddCost(lines, LedgerCategory.Fuel, fuelPrice * (decimal)uplift, L.F("Fuel uplift {0:N0} kg at €{1:0.000}/kg", uplift, fuelPrice));
        else if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Fuel, fuelPrice * (decimal)r.FuelUsedKg, L.F("Fuel {0:N0} kg at €{1:0.000}/kg", r.FuelUsedKg, fuelPrice));
        if (airHours > 0)
            AddCost(lines, LedgerCategory.LandingFee, CostModel.LandingFee(t, i.Profile, landedAt), L.F("Landing fee {0}", landedAt.Ident));
        if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Handling,
                CostModel.Handling(t, i.Origin, c.TotalPassengers, c.CargoKg + c.BaggageKg) + CostModel.Handling(t, landedAt, c.TotalPassengers, c.CargoKg + c.BaggageKg),
                L.F("Handling {0} / {1}", i.Origin.Ident, landedAt.Ident));
        AddCost(lines, LedgerCategory.Navigation, CostModel.Navigation(t, i.Profile, r.DistanceFlownNm, i.Type.FlightRules != FlightRulesRequirement.Vfr),
            L.T("Route charges"));
        if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Catering, CostModel.Catering(t, c.TotalPassengers, blockHours), L.T("Catering"));
        else
            notes.Add(L.T("Fuel, handling and catering as per GSX receipts."));
        AddCost(lines, LedgerCategory.Crew, CostModel.Crew(t, i.Profile, blockHours), L.T("Crew"));

        if (i.Aircraft.Ownership == Ownership.Rented)
            AddCost(lines, LedgerCategory.Rent, i.Aircraft.RentPerBlockHour * (decimal)Math.Max(1, blockHours), L.F("Rent {0:0.0} h", Math.Max(1, blockHours)));
        else
            AddCost(lines, LedgerCategory.Maintenance, CostModel.Maintenance(t, i.Profile, airHours, i.Aircraft.EffectiveCondition), L.F("Maintenance reserve {0:0.0} FH", airHours));

        double conditionLoss = outcome == FlightOutcome.Crashed ? 60 : FleetEconomics.ConditionLoss(airHours, r.TouchdownFpm, r.MaxGForce);
        if (outcome != FlightOutcome.Crashed && r.SimWearDelta is { IsEmpty: false } wear)
        {
            double simLoss = FleetEconomics.SimConditionLoss(wear);
            conditionLoss += simLoss;
            var worn = wear.GroupLosses().Where(g => g.Loss >= 0.00005).Take(3)
                .Select(g => $"{g.Group} −{g.Loss * 100:0.##} %").ToList();
            notes.Add(worn.Count == 0
                ? L.T("MSFS wear: none measurable.")
                : L.F("MSFS wear: {0} (condition −{1:0.##} %).", string.Join(", ", worn), simLoss));
        }
        if (Math.Abs(r.TouchdownFpm) > 600) notes.Add(L.F("Hard landing ({0:N0} fpm) – inspection recommended.", r.TouchdownFpm));

        return new SettlementResult(outcome, score, lines, landedAt.Ident, arrivalTime, blockHours, airHours,
            conditionLoss, reputation, notes);
    }

    private static void AddCost(List<CostLine> lines, LedgerCategory category, decimal cost, string description)
    {
        if (cost > 0) lines.Add(new CostLine(category, -Math.Round(cost, 2), description));
    }
}
