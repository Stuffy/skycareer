using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.World;

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
    double? FuelUpliftKg = null);     // fuel stays in the aircraft: pay what was added since the last flight, not what was burnt

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
            { SevereViolations: > 0 } => FlightOutcome.Failed,
            _ when !atDestination => FlightOutcome.Diverted,
            _ when lateBy > TimeSpan.Zero => FlightOutcome.Late,
            _ => FlightOutcome.Completed
        };

        var score = outcome is FlightOutcome.Crashed or FlightOutcome.Failed
            ? new FlightScore(0, 0, 0, 0, 0)
            : FlightScorer.Score(i.Type.Scoring, r, lateBy);

        // Revenue
        int reputation;
        switch (outcome)
        {
            case FlightOutcome.Completed:
            case FlightOutcome.Late:
                lines.Add(new(LedgerCategory.MissionRevenue, c.Payout, $"{i.Type.Name} {c.Origin}→{c.Destination}"));
                if (outcome == FlightOutcome.Completed && i.Type.Pay.OnTimeBonus > 0)
                    lines.Add(new(LedgerCategory.Bonus, Math.Round(c.Payout * i.Type.Pay.OnTimeBonus, 0), "Pünktlichkeitsbonus"));
                if (outcome == FlightOutcome.Late)
                {
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.25m, 0), $"Verspätung {lateBy.TotalMinutes:N0} min"));
                    notes.Add($"Deadline um {lateBy.TotalMinutes:N0} min überschritten.");
                }
                if (score.Total < 50)
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.2m, 0), $"Kundenbeschwerde (Score {score.Total})"));
                reputation = outcome == FlightOutcome.Late ? -1 : 1 + (score.Total >= 85 ? 1 : 0) + (score.Total >= 95 ? 1 : 0);
                foreach (var issue in r.DepartureIssues.Take(3))
                {
                    lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * DepartureIssuePenalty, 0), $"Abweichung beim Abflug: {issue}"));
                    notes.Add($"Beim Abheben nicht in Ordnung: {issue}");
                }
                if (r.DepartureIssues.Count > 0) reputation -= 1;
                break;
            case FlightOutcome.Diverted:
                notes.Add($"Nicht am Ziel {c.Destination} angekommen (Endposition {landedAt.Ident}). Keine Vergütung.");
                reputation = -2;
                break;
            case FlightOutcome.Crashed:
                lines.Add(new(LedgerCategory.Penalty, -Math.Round(c.Payout * 0.2m, 0), "Vertragsstrafe nach Unfall"));
                reputation = -10;
                break;
            default:
                notes.Add("Auftrag wegen Regelverstoß (Slew, Teleport, Tanken im Flug, Lade- oder Flugzeugwechsel) annulliert.");
                reputation = -5;
                break;
        }

        // Variable costs are always due: the flight happened, whatever the outcome.
        var fuelPrice = CostModel.FuelPricePerKg(t, i.Profile.FuelType, i.Origin, i.DepartureGameTime);
        if (!i.ExternalGroundCosts && i.FuelUpliftKg is { } uplift)
            AddCost(lines, LedgerCategory.Fuel, fuelPrice * (decimal)uplift, $"Getankt {uplift:N0} kg à {fuelPrice:0.000} €/kg");
        else if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Fuel, fuelPrice * (decimal)r.FuelUsedKg, $"Treibstoff {r.FuelUsedKg:N0} kg à {fuelPrice:0.000} €/kg");
        if (airHours > 0)
            AddCost(lines, LedgerCategory.LandingFee, CostModel.LandingFee(t, i.Profile, landedAt), $"Landegebühr {landedAt.Ident}");
        if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Handling,
                CostModel.Handling(t, i.Origin, c.TotalPassengers, c.CargoKg + c.BaggageKg) + CostModel.Handling(t, landedAt, c.TotalPassengers, c.CargoKg + c.BaggageKg),
                $"Abfertigung {i.Origin.Ident} / {landedAt.Ident}");
        AddCost(lines, LedgerCategory.Navigation, CostModel.Navigation(t, i.Profile, r.DistanceFlownNm, i.Type.FlightRules != FlightRulesRequirement.Vfr),
            "Streckengebühren");
        if (!i.ExternalGroundCosts)
            AddCost(lines, LedgerCategory.Catering, CostModel.Catering(t, c.TotalPassengers, blockHours), "Catering");
        else
            notes.Add("Treibstoff, Abfertigung und Catering laut GSX-Belegen.");
        AddCost(lines, LedgerCategory.Crew, CostModel.Crew(t, i.Profile, blockHours), "Crew");

        if (i.Aircraft.Ownership == Ownership.Rented)
            AddCost(lines, LedgerCategory.Rent, i.Aircraft.RentPerBlockHour * (decimal)Math.Max(1, blockHours), $"Miete {Math.Max(1, blockHours):0.0} h");
        else
            AddCost(lines, LedgerCategory.Maintenance, CostModel.Maintenance(t, i.Profile, airHours, i.Aircraft.EffectiveCondition), $"Wartungsrücklage {airHours:0.0} FH");

        double conditionLoss = outcome == FlightOutcome.Crashed ? 60 : FleetEconomics.ConditionLoss(airHours, r.TouchdownFpm, r.MaxGForce);
        if (outcome != FlightOutcome.Crashed && r.SimWearDelta is { IsEmpty: false } wear)
        {
            double simLoss = FleetEconomics.SimConditionLoss(wear);
            conditionLoss += simLoss;
            var worn = wear.GroupLosses().Where(g => g.Loss >= 0.00005).Take(3)
                .Select(g => $"{g.Group} −{g.Loss * 100:0.##} %").ToList();
            notes.Add(worn.Count == 0
                ? "Abnutzung laut MSFS: keine messbare."
                : $"Abnutzung laut MSFS: {string.Join(", ", worn)} (Zustand −{simLoss:0.##} %).");
        }
        if (Math.Abs(r.TouchdownFpm) > 600) notes.Add($"Harte Landung ({r.TouchdownFpm:N0} fpm) – Inspektion empfohlen.");

        return new SettlementResult(outcome, score, lines, landedAt.Ident, arrivalTime, blockHours, airHours,
            conditionLoss, reputation, notes);
    }

    private static void AddCost(List<CostLine> lines, LedgerCategory category, decimal cost, string description)
    {
        if (cost > 0) lines.Add(new CostLine(category, -Math.Round(cost, 2), description));
    }
}
