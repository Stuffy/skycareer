using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Departure;

public enum Light { Unknown, Green, Yellow, Red }

/// <summary>One line of the departure traffic light. Label and detail are localized display texts.</summary>
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
        Limits = "limits", Ofp = "ofp", Availability = "availability", Technical = "technical";

    /// <summary>Aircraft must be on the ground this close to the departure airport's reference point.</summary>
    public const double MaxDistanceNm = 3;

    public static DepartureReadiness Evaluate(MissionContract contract, MissionType type, AircraftProfile profile, FleetAircraft aircraft,
        LoadSheet sheet, Airport origin, DateTime gameTime, SimAircraftState? sim)
    {
        var items = new List<CheckItem>();

        if (sim is null)
        {
            items.Add(new(Sim, L.T("Simulator"), Light.Unknown, L.T("not connected")));
            foreach (var (key, label) in new[] { (Type, L.T("Aircraft type")), (Position, L.T("Location")), (Payload, L.T("Payload")), (Fuel, L.T("Fuel")), (Limits, L.T("Weight limits")) })
                items.Add(new(key, label, Light.Unknown, "–"));
        }
        else
        {
            items.Add(new(Sim, L.T("Simulator"), Light.Green, L.T("connected")));

            items.Add(profile.MatchesSimTitle(sim.Title)
                ? new(Type, L.T("Aircraft type"), Light.Green, L.F("{0} (“{1}”)", profile.Name, sim.Title))
                : new(Type, L.T("Aircraft type"), Light.Red,
                    L.F("“{0}” in the sim, {1} required. If this is the right type, add it to the profile's sim matching.", sim.Title, profile.Name)));

            double distance = sim.Position.DistanceNmTo(origin.Position);
            items.Add(distance <= MaxDistanceNm && sim.OnGround
                ? new(Position, L.T("Location"), Light.Green, L.F("on the ground at {0}", origin.Ident))
                : new(Position, L.T("Location"), Light.Red, sim.OnGround
                    ? L.F("{0:N0} nm away from {1}", distance, origin.Ident)
                    : L.F("airborne, {0:N0} nm from {1}", distance, origin.Ident)));

            var expectedPayload = sheet.SimPayload;
            double payloadDiff = sim.PayloadKg - expectedPayload.Kg, tolerance = LoadVerifier.ToleranceKg(expectedPayload);
            items.Add(new(Payload, L.T("Payload"),
                Math.Abs(payloadDiff) <= tolerance ? Light.Green : Math.Abs(payloadDiff) <= tolerance * 3 ? Light.Yellow : Light.Red,
                L.F("Sim {0:N0} kg, planned {1:N0} kg (incl. crew)", sim.PayloadKg, expectedPayload.Kg)));

            // Taxi fuel is planned to be burnt before takeoff, so anything down to block minus taxi is still on plan.
            var block = sheet.Fuel.Block;
            double fuelTolerance = LoadVerifier.ToleranceKg(block);
            double onPlan = block.Kg - sheet.Fuel.Taxi.Kg - fuelTolerance;
            double minimum = sheet.Fuel.MinimumTakeoff.Kg;
            var fuelLight = sim.FuelKg >= onPlan
                ? sim.FuelKg <= block.Kg + Math.Max(fuelTolerance, block.Kg * 0.1) ? Light.Green : Light.Yellow
                : sim.FuelKg >= minimum - fuelTolerance ? Light.Yellow : Light.Red;
            string fuelDetail = L.F("Sim {0:N0} kg, planned block {1:N0} kg (incl. {2:N0} kg taxi)", sim.FuelKg, block.Kg, sheet.Fuel.Taxi.Kg) + fuelLight switch
            {
                Light.Red => L.F(" – below the minimum takeoff fuel ({0:N0} kg)", minimum),
                Light.Yellow when sim.FuelKg > block.Kg => L.T(" – more than planned"),
                Light.Yellow => L.T(" – less than planned, minimum fuel covered"),
                _ => ""
            };
            items.Add(new(Fuel, L.T("Fuel"), fuelLight, fuelDetail));

            double zfw = sim.TotalWeightKg - sim.FuelKg, tow = sim.TotalWeightKg - Math.Min(sim.FuelKg, sheet.Fuel.Taxi.Kg);
            var over = new List<string>();
            if (zfw > profile.MaxZeroFuelKg + 5) over.Add($"ZFW {zfw:N0} > MZFW {profile.MaxZeroFuelKg:N0} kg");
            if (tow > profile.MaxTakeoffKg + 5) over.Add($"TOW {tow:N0} > MTOW {profile.MaxTakeoffKg:N0} kg");
            items.Add(over.Count == 0
                ? new(Limits, L.T("Weight limits"), Light.Green, $"ZFW {zfw:N0} / {profile.MaxZeroFuelKg:N0} kg · TOW {tow:N0} / {profile.MaxTakeoffKg:N0} kg")
                : new(Limits, L.T("Weight limits"), Light.Red, string.Join(" · ", over)));
        }

        if (type.SimBrief != SimBriefUsage.None)
        {
            items.Add(contract.OfpBlockFuelKg is not null
                ? new(Ofp, L.T("SimBrief OFP"), Light.Green, L.T("loaded"))
                : type.SimBrief == SimBriefUsage.Required
                    ? new(Ofp, L.T("SimBrief OFP"), Light.Red, L.T("missing – required for this job"))
                    : new(Ofp, L.T("SimBrief OFP"), Light.Yellow, L.T("missing (optional)")));
        }

        var plannedArrival = gameTime.AddHours(contract.DistanceNm / Math.Max(1, profile.CruiseKtas) + 0.5);
        items.Add(!aircraft.IsAvailable(gameTime)
            ? new(Availability, L.T("Availability"), Light.Red, L.F("{0} until {1:dd.MM. HH:mm}", aircraft.UnavailableReason ?? L.T("Maintenance"), aircraft.UnavailableUntil))
            : plannedArrival > contract.Deadline
                ? new(Availability, L.T("Availability"), Light.Yellow, L.F("Deadline {0:dd.MM. HH:mm} is hardly achievable even with an immediate departure", contract.Deadline))
                : new(Availability, L.T("Availability"), Light.Green, L.F("Deadline {0:dd.MM. HH:mm}", contract.Deadline)));

        // Worn components only warn (never block or cost payout); shown once the aircraft has wear data from the sim.
        var components = aircraft.Components;
        if (!components.IsEmpty)
        {
            var worn = components.Warnings();
            items.Add(worn.Count == 0
                ? new(Technical, L.T("Technical"), Light.Green, L.F("all components ≥ {0:P0}", WearComponents.WarningLevel))
                : new(Technical, L.T("Technical"), Light.Yellow,
                    L.F("{0} – repair recommended", string.Join(", ", worn.Select(w => $"{w.Group} {w.Level:P0}")))));
        }

        return new DepartureReadiness(items);
    }
}
