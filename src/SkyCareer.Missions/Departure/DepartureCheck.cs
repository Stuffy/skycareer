using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;

namespace SkyCareer.Missions.Departure;

public enum Light { Unknown, Green, Yellow, Red }

/// <summary>One line of the departure traffic light. Texts are shown in the (German) UI as-is.</summary>
public sealed record CheckItem(string Key, string Label, Light Light, string Detail);

/// <summary>What the simulator reports about the loaded aircraft (simulator independent).</summary>
public sealed record SimAircraftState(string Title, GeoPosition Position, bool OnGround, double PayloadKg, double FuelKg, double TotalWeightKg);

public sealed record DepartureReadiness(IReadOnlyList<CheckItem> Items)
{
    /// <summary>
    /// The worst light (enum order Unknown &lt; Green &lt; Yellow &lt; Red). Without simulator data only a red item counts:
    /// a yellow OFP hint alone must not look like "ready".
    /// </summary>
    public Light Overall
    {
        get
        {
            var worst = Items.Select(i => i.Light).DefaultIfEmpty(Light.Unknown).Max();
            return this[DepartureCheck.Sim]?.Light == Light.Unknown && worst != Light.Red ? Light.Unknown : worst;
        }
    }

    public CheckItem? this[string key] => Items.FirstOrDefault(i => i.Key == key);

    /// <summary>Red at takeoff means the job doesn't start: flying from the wrong place or in the wrong aircraft.</summary>
    public IEnumerable<CheckItem> Blocking => Items.Where(i => i.Light == Light.Red && i.Key is DepartureCheck.Position or DepartureCheck.Type);

    /// <summary>Red at takeoff that costs money at settlement (load, fuel, weight limits, OFP).</summary>
    public IEnumerable<CheckItem> Penalised => Items.Where(i => i.Light == Light.Red && i.Key is DepartureCheck.Payload or DepartureCheck.Fuel
        or DepartureCheck.Limits or DepartureCheck.Ofp);
}

/// <summary>
/// The departure traffic light: does the aircraft in the sim match the job (type, position, load, fuel, limits,
/// OFP, availability)? Pure function so the market card, the live page and the takeoff decision all agree.
/// </summary>
public static class DepartureCheck
{
    public const string Sim = "sim", Type = "type", Position = "position", Payload = "payload", Fuel = "fuel",
        Limits = "limits", Ofp = "ofp", Availability = "availability";

    /// <summary>Aircraft must be on the ground this close to the departure airport's reference point.</summary>
    public const double MaxDistanceNm = 3;

    public static DepartureReadiness Evaluate(MissionContract contract, MissionType type, AircraftProfile profile, FleetAircraft aircraft,
        LoadSheet sheet, Airport origin, DateTime gameTime, SimAircraftState? sim)
    {
        var items = new List<CheckItem>();

        if (sim is null)
        {
            items.Add(new(Sim, "Simulator", Light.Unknown, "nicht verbunden"));
            foreach (var (key, label) in new[] { (Type, "Flugzeugtyp"), (Position, "Standort"), (Payload, "Zuladung"), (Fuel, "Treibstoff"), (Limits, "Gewichtsgrenzen") })
                items.Add(new(key, label, Light.Unknown, "–"));
        }
        else
        {
            items.Add(new(Sim, "Simulator", Light.Green, "verbunden"));

            items.Add(profile.MatchesSimTitle(sim.Title)
                ? new(Type, "Flugzeugtyp", Light.Green, $"{profile.Name} („{sim.Title}“)")
                : new(Type, "Flugzeugtyp", Light.Red,
                    $"Im Sim „{sim.Title}“, verlangt {profile.Name}. Falls das der richtige Typ ist: Sim-Erkennung im Profil ergänzen."));

            double distance = sim.Position.DistanceNmTo(origin.Position);
            items.Add(distance <= MaxDistanceNm && sim.OnGround
                ? new(Position, "Standort", Light.Green, $"am Boden in {origin.Ident}")
                : new(Position, "Standort", Light.Red, sim.OnGround
                    ? $"{distance:N0} nm von {origin.Ident} entfernt"
                    : $"in der Luft, {distance:N0} nm von {origin.Ident}"));

            var expectedPayload = sheet.SimPayload;
            double payloadDiff = sim.PayloadKg - expectedPayload.Kg, tolerance = LoadVerifier.ToleranceKg(expectedPayload);
            items.Add(new(Payload, "Zuladung",
                Math.Abs(payloadDiff) <= tolerance ? Light.Green : Math.Abs(payloadDiff) <= tolerance * 3 ? Light.Yellow : Light.Red,
                $"Sim {sim.PayloadKg:N0} kg, Soll {expectedPayload.Kg:N0} kg (inkl. Crew)"));

            // Taxi fuel is planned to be burnt before takeoff, so anything down to block minus taxi is still on plan.
            var block = sheet.Fuel.Block;
            double fuelTolerance = LoadVerifier.ToleranceKg(block);
            double onPlan = block.Kg - sheet.Fuel.Taxi.Kg - fuelTolerance;
            double minimum = sheet.Fuel.MinimumTakeoff.Kg;
            var fuelLight = sim.FuelKg >= onPlan
                ? sim.FuelKg <= block.Kg + Math.Max(fuelTolerance, block.Kg * 0.1) ? Light.Green : Light.Yellow
                : sim.FuelKg >= minimum - fuelTolerance ? Light.Yellow : Light.Red;
            string fuelDetail = $"Sim {sim.FuelKg:N0} kg, geplant {block.Kg:N0} kg (inkl. {sheet.Fuel.Taxi.Kg:N0} kg Rollen)" + fuelLight switch
            {
                Light.Red => $" – unter dem Mindestbedarf für den Start ({minimum:N0} kg)",
                Light.Yellow when sim.FuelKg > block.Kg => " – mehr als geplant",
                Light.Yellow => " – weniger als geplant, Mindestbedarf gedeckt",
                _ => ""
            };
            items.Add(new(Fuel, "Treibstoff", fuelLight, fuelDetail));

            double zfw = sim.TotalWeightKg - sim.FuelKg, tow = sim.TotalWeightKg - Math.Min(sim.FuelKg, sheet.Fuel.Taxi.Kg);
            var over = new List<string>();
            if (zfw > profile.MaxZeroFuelKg + 5) over.Add($"ZFW {zfw:N0} > MZFW {profile.MaxZeroFuelKg:N0} kg");
            if (tow > profile.MaxTakeoffKg + 5) over.Add($"TOW {tow:N0} > MTOW {profile.MaxTakeoffKg:N0} kg");
            items.Add(over.Count == 0
                ? new(Limits, "Gewichtsgrenzen", Light.Green, $"ZFW {zfw:N0} / {profile.MaxZeroFuelKg:N0} kg · TOW {tow:N0} / {profile.MaxTakeoffKg:N0} kg")
                : new(Limits, "Gewichtsgrenzen", Light.Red, string.Join(" · ", over)));
        }

        if (type.SimBrief != SimBriefUsage.None)
        {
            items.Add(contract.OfpBlockFuelKg is not null
                ? new(Ofp, "SimBrief-OFP", Light.Green, "übernommen")
                : type.SimBrief == SimBriefUsage.Required
                    ? new(Ofp, "SimBrief-OFP", Light.Red, "fehlt – für diesen Auftrag vorgeschrieben")
                    : new(Ofp, "SimBrief-OFP", Light.Yellow, "fehlt (optional)"));
        }

        var plannedArrival = gameTime.AddHours(contract.DistanceNm / Math.Max(1, profile.CruiseKtas) + 0.5);
        items.Add(!aircraft.IsAvailable(gameTime)
            ? new(Availability, "Verfügbarkeit", Light.Red, $"{aircraft.UnavailableReason ?? "Wartung"} bis {aircraft.UnavailableUntil:dd.MM. HH:mm}")
            : plannedArrival > contract.Deadline
                ? new(Availability, "Verfügbarkeit", Light.Yellow, $"Deadline {contract.Deadline:dd.MM. HH:mm} ist bei sofortigem Start kaum zu schaffen")
                : new(Availability, "Verfügbarkeit", Light.Green, $"Deadline {contract.Deadline:dd.MM. HH:mm}"));

        return new DepartureReadiness(items);
    }
}
