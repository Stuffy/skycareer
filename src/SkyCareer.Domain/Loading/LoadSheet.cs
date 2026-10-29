using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Domain.Loading;

public sealed record StationLoad(int Index, string Name, Mass Mass);

public enum LimitViolation { SeatsExceeded, CargoExceeded, ZeroFuelWeight, TakeoffWeight, LandingWeight, FuelCapacity }

/// <summary>
/// Final weight and balance figures for one flight. Crew mass is listed separately because in the sim it
/// sits in payload stations, while commercially it isn't payload.
/// </summary>
public sealed record LoadSheet
{
    public required AircraftProfile Aircraft { get; init; }
    public required PassengerCount Passengers { get; init; }
    public required Mass PassengerMass { get; init; }
    public required Mass BaggageMass { get; init; }
    public required Mass CargoMass { get; init; }
    public required FuelPlan Fuel { get; init; }
    public required IReadOnlyList<StationLoad> Stations { get; init; }

    public Mass Payload => PassengerMass + BaggageMass + CargoMass;
    public Mass Crew => Aircraft.CrewMass;
    /// <summary>What the sim should report as payload (TOTAL − EMPTY − FUEL): crew stations included.</summary>
    public Mass SimPayload => Payload + Crew;
    public Mass ZeroFuelWeight => Aircraft.OperatingEmpty + Crew + Payload;
    public Mass TakeoffWeight => ZeroFuelWeight + Fuel.Takeoff;
    public Mass LandingWeight => TakeoffWeight - Fuel.Trip;

    public IReadOnlyList<LimitViolation> Violations
    {
        get
        {
            var v = new List<LimitViolation>();
            if (Passengers.Seated > Aircraft.PassengerSeats) v.Add(LimitViolation.SeatsExceeded);
            if ((BaggageMass + CargoMass).Kg > Aircraft.MaxCargoKg + 0.5) v.Add(LimitViolation.CargoExceeded);
            if (ZeroFuelWeight > Aircraft.MaxZeroFuel) v.Add(LimitViolation.ZeroFuelWeight);
            if (TakeoffWeight > Aircraft.MaxTakeoff) v.Add(LimitViolation.TakeoffWeight);
            if (LandingWeight > Aircraft.MaxLanding) v.Add(LimitViolation.LandingWeight);
            if (Fuel.Block > Aircraft.MaxFuel) v.Add(LimitViolation.FuelCapacity);
            return v;
        }
    }

    public bool IsWithinLimits => Violations.Count == 0;
}

public static class LoadSheetBuilder
{
    /// <param name="baggageMode">Defaults to <paramref name="mode"/>; offers keep passengers at standard mass (as SimBrief
    /// weighs them) but vary the bags.</param>
    public static LoadSheet Build(AircraftProfile aircraft, PassengerCount pax, BaggageProfile baggage, Mass cargo,
        FuelPlan fuel, PassengerMassMode mode = PassengerMassMode.Standard, PassengerMassTable? table = null, Random? rng = null,
        PassengerMassMode? baggageMode = null)
    {
        table ??= PassengerMassTable.Default;
        rng ??= Random.Shared;

        var paxMass = table.PassengerMass(pax, mode, rng);
        var bagMass = table.BaggageMass(pax, baggage, baggageMode ?? mode, rng);
        return FromMasses(aircraft, pax, paxMass, bagMass, cargo, fuel);
    }

    /// <summary>Rebuilds a load sheet from already drawn masses (e.g. frozen into an accepted contract).</summary>
    public static LoadSheet FromMasses(AircraftProfile aircraft, PassengerCount pax, Mass paxMass, Mass bagMass, Mass cargo, FuelPlan fuel)
    {
        return new LoadSheet
        {
            Aircraft = aircraft,
            Passengers = pax,
            PassengerMass = paxMass,
            BaggageMass = bagMass,
            CargoMass = cargo,
            Fuel = fuel,
            Stations = DistributeToStations(aircraft, pax, paxMass, bagMass + cargo)
        };
    }

    /// <summary>
    /// Puts crew on crew stations, spreads passengers over passenger stations by seat count and hold load
    /// over baggage/cargo stations by capacity.
    /// </summary>
    public static IReadOnlyList<StationLoad> DistributeToStations(AircraftProfile aircraft, PassengerCount pax, Mass paxMass, Mass holdMass)
    {
        var result = new List<StationLoad>();
        var crewStations = aircraft.Stations.Where(s => s.Kind == StationKind.Crew).ToList();
        var paxStations = aircraft.Stations.Where(s => s.Kind == StationKind.Passengers).ToList();
        var holdStations = aircraft.Stations.Where(s => s.Kind is StationKind.Baggage or StationKind.Cargo).ToList();

        int crewSeats = crewStations.Sum(s => Math.Max(1, s.Seats));
        foreach (var s in crewStations)
            result.Add(new StationLoad(s.Index, s.Name, aircraft.CrewMass * ((double)Math.Max(1, s.Seats) / crewSeats)));

        // Fill seats front to back so a half-full cabin is realistic, then convert seats to mass.
        double massPerSeated = pax.Seated > 0 ? paxMass.Kg / pax.Seated : 0;
        int remainingPax = pax.Seated;
        foreach (var s in paxStations)
        {
            int here = Math.Min(remainingPax, s.Seats);
            remainingPax -= here;
            result.Add(new StationLoad(s.Index, s.Name, Mass.FromKg(here * massPerSeated)));
        }
        // Seats beyond station capacity (bad profile) are put on the last station rather than lost.
        if (remainingPax > 0 && result.Count > 0)
            result[^1] = result[^1] with { Mass = result[^1].Mass + Mass.FromKg(remainingPax * massPerSeated) };

        double totalCap = holdStations.Sum(s => s.MaxKg);
        foreach (var s in holdStations)
        {
            double share = totalCap > 0 ? s.MaxKg / totalCap : 1.0 / holdStations.Count;
            result.Add(new StationLoad(s.Index, s.Name, holdMass * share));
        }

        return result;
    }
}
