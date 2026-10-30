using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;

namespace SkyCareer.Domain.Finance;

/// <summary>Tunable economy figures. Defaults are rough European 2026 levels; a career can scale them by difficulty.</summary>
public sealed record EconomyTables
{
    public decimal JetAPerKg { get; init; } = 0.95m;
    public decimal AvGasPerKg { get; init; } = 2.80m;
    /// <summary>Daily fuel price swing per airport (±).</summary>
    public double FuelPriceVariation { get; init; } = 0.08;

    public decimal CateringPerPaxShort { get; init; } = 1.5m;
    public decimal CateringPerPaxLong { get; init; } = 4m;
    public decimal CopilotPerBlockHour { get; init; } = 70m;
    public decimal CabinCrewPerBlockHour { get; init; } = 35m;
    /// <summary>En-route charge per 100 km, scaled by sqrt(MTOW t / 50) like EUROCONTROL's weight factor.</summary>
    public decimal NavigationRatePer100Km { get; init; } = 60m;
    public decimal CargoHandlingPerKg { get; init; } = 0.08m;

    /// <summary>Multiplier on all costs (Easy 0.8, Normal 1, Hard 1.2).</summary>
    public decimal CostFactor { get; init; } = 1m;

    public static EconomyTables For(Difficulty d) => new()
    {
        CostFactor = d switch { Difficulty.Easy => 0.8m, Difficulty.Hard => 1.2m, _ => 1m }
    };

    public static decimal StartingCash(Difficulty d) => d switch
    {
        Difficulty.Easy => 750_000m,
        Difficulty.Hard => 150_000m,
        _ => 350_000m
    };
}

public sealed record CostLine(LedgerCategory Category, decimal Amount, string Description);

/// <summary>Computes the variable costs of a single flight. All amounts are returned as positive costs.</summary>
public static class CostModel
{
    public static decimal FuelPricePerKg(EconomyTables t, FuelType fuel, Airport airport, DateTime gameTime)
    {
        decimal basePrice = fuel == FuelType.AvGas ? t.AvGasPerKg : t.JetAPerKg;
        decimal sizeFactor = airport.Size switch
        {
            AirportSize.Large => 1.00m,
            AirportSize.Medium => 1.08m,
            AirportSize.Small => 1.20m,
            _ => 1.25m
        };
        // Deterministic per airport and day (also across app restarts, so no string.GetHashCode) so the price
        // shown before the flight is the one charged after it.
        double swing = (new Random(StableSeed(airport.Ident, gameTime)).NextDouble() * 2 - 1) * t.FuelPriceVariation;
        return Math.Round(basePrice * sizeFactor * (decimal)(1 + swing) * t.CostFactor, 3);
    }

    private static int StableSeed(string ident, DateTime day)
    {
        uint hash = 2166136261;
        foreach (char c in ident.ToUpperInvariant()) hash = (hash ^ c) * 16777619;
        hash = (hash ^ (uint)DateOnly.FromDateTime(day).DayNumber) * 16777619;
        return (int)(hash & 0x7FFFFFFF);
    }

    public static decimal LandingFee(EconomyTables t, AircraftProfile a, Airport airport)
    {
        double tonnes = a.MaxTakeoffKg / 1000.0;
        (decimal fixedFee, decimal perTonne) = airport.Size switch
        {
            AirportSize.Large => (250m, 9m),
            AirportSize.Medium => (120m, 6m),
            AirportSize.Small => (25m, 4m),
            _ => (15m, 3m)
        };
        return Math.Round((fixedFee + perTonne * (decimal)tonnes) * t.CostFactor, 2);
    }

    public static decimal Handling(EconomyTables t, Airport airport, int passengers, double cargoKg)
    {
        decimal perPax = airport.Size switch { AirportSize.Large => 8m, AirportSize.Medium => 6m, AirportSize.Small => 2m, _ => 0m };
        decimal turnaround = airport.Size switch { AirportSize.Large => 180m, AirportSize.Medium => 90m, AirportSize.Small => 20m, _ => 0m };
        return Math.Round((turnaround + perPax * passengers + t.CargoHandlingPerKg * (decimal)cargoKg) * t.CostFactor, 2);
    }

    public static decimal Navigation(EconomyTables t, AircraftProfile a, double distanceNm, bool ifr)
    {
        if (!ifr || a.MaxTakeoffKg < 2000) return 0;
        double km = distanceNm * 1.852;
        double weightFactor = Math.Sqrt(a.MaxTakeoffKg / 1000.0 / 50.0);
        return Math.Round(t.NavigationRatePer100Km * (decimal)(km / 100 * weightFactor) * t.CostFactor, 2);
    }

    public static decimal Catering(EconomyTables t, int passengers, double blockHours) =>
        Math.Round((blockHours > 1 ? t.CateringPerPaxLong : t.CateringPerPaxShort) * passengers * t.CostFactor, 2);

    /// <summary>The player flies as captain; additional flight crew and cabin crew are paid per block hour.</summary>
    public static decimal Crew(EconomyTables t, AircraftProfile a, double blockHours) =>
        Math.Round((Math.Max(0, a.Pilots - 1) * t.CopilotPerBlockHour + a.CabinCrewCount * t.CabinCrewPerBlockHour)
                   * (decimal)blockHours * t.CostFactor, 2);

    /// <summary>Worn aircraft cost more to keep flying: up to +50 % at condition 0.</summary>
    public static decimal Maintenance(EconomyTables t, AircraftProfile a, double flightHours, double condition) =>
        Math.Round(a.Economics.MaintenancePerFlightHour * (decimal)flightHours * (1 + (decimal)(100 - condition) / 200m) * t.CostFactor, 2);

    /// <summary>
    /// Costs of a ferry flight without passengers, flown by a hired crew (all pilots paid, no cabin crew):
    /// fuel, fees, crew and rent or maintenance reserve. Climb/descent add 0.2 h, taxi 0.3 h of block time.
    /// </summary>
    public static FerryEstimate Ferry(EconomyTables t, AircraftProfile a, FleetAircraft aircraft, Airport from, Airport to, DateTime gameTime)
    {
        double distance = from.Position.DistanceNmTo(to.Position);
        double airHours = distance / Math.Max(1, a.CruiseKtas) + 0.2;
        double blockHours = airHours + 0.3;
        double fuelKg = a.FuelBurnKgPerHour * (airHours + 0.1);
        var price = FuelPricePerKg(t, a.FuelType, from, gameTime);
        string route = $"{from.Ident}→{to.Ident}";
        var lines = new List<CostLine>
        {
            new(LedgerCategory.Fuel, Math.Round(price * (decimal)fuelKg, 2), $"Überführung {route}: Treibstoff {fuelKg:N0} kg"),
            new(LedgerCategory.LandingFee, LandingFee(t, a, to), $"Überführung {route}: Landegebühr {to.Ident}"),
            new(LedgerCategory.Handling, Handling(t, from, 0, 0) + Handling(t, to, 0, 0), $"Überführung {route}: Abfertigung"),
            new(LedgerCategory.Navigation, Navigation(t, a, distance, true), $"Überführung {route}: Flugsicherung"),
            new(LedgerCategory.Crew, Math.Round(a.Pilots * t.CopilotPerBlockHour * (decimal)blockHours * t.CostFactor, 2),
                $"Überführung {route}: Überführungscrew"),
            aircraft.Ownership == Ownership.Rented
                ? new(LedgerCategory.Rent, Math.Round(aircraft.RentPerBlockHour * (decimal)Math.Max(1, blockHours), 2),
                    $"Überführung {route}: Miete {Math.Max(1, blockHours):0.0} h")
                : new(LedgerCategory.Maintenance, Maintenance(t, a, airHours, aircraft.EffectiveCondition),
                    $"Überführung {route}: Wartungsrücklage {airHours:0.0} FH")
        };
        return new FerryEstimate(distance, blockHours, airHours, lines.Where(l => l.Amount > 0).ToList());
    }
}

public sealed record FerryEstimate(double DistanceNm, double BlockHours, double AirHours, IReadOnlyList<CostLine> Costs)
{
    public decimal Total => Costs.Sum(c => c.Amount);
}
