using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;

namespace SkyCareer.Domain.Finance;

/// <summary>Values, wear and scheduled checks of fleet aircraft.</summary>
public static class FleetEconomics
{
    /// <summary>
    /// What the market pays for an hour of this aircraft: its rental rate plus the fuel it burns at list price.
    /// Mission types pay a share of it (<c>pay.hourlyRateFactor</c>), roughly matching real charter rates
    /// (Caravan ~1.300 €/h, King Air 350 ~2.900 €/h, Citation X ~3.700 €/h before the mission's factor).
    /// </summary>
    public static decimal HourlyRate(AircraftProfile a, EconomyTables? tables = null)
    {
        var t = tables ?? new EconomyTables();
        decimal fuelPerKg = a.FuelType == FuelType.AvGas ? t.AvGasPerKg : t.JetAPerKg;
        return a.Economics.RentPerBlockHour + fuelPerKg * (decimal)a.FuelBurnKgPerHour;
    }

    /// <summary>Hours between scheduled checks: 100 h inspection for pistons, ~600 h (A-check-like) otherwise.</summary>
    public static double CheckIntervalHours(AircraftProfile a) => a.FuelType == FuelType.AvGas ? 100 : 600;

    public static decimal CheckCost(AircraftProfile a) => a.Economics.MaintenancePerFlightHour * 20;

    public static TimeSpan CheckDowntime(AircraftProfile a) =>
        a.MaxTakeoffKg switch { < 5_700 => TimeSpan.FromDays(1), < 50_000 => TimeSpan.FromDays(2), _ => TimeSpan.FromDays(3) };

    /// <summary>Market value: 6 % depreciation per year of ownership plus wear, never below 25 % of the new price.</summary>
    public static decimal MarketValue(AircraftProfile a, FleetAircraft f, DateTime gameTime)
    {
        double years = Math.Max(0, (gameTime - f.AcquiredAt).TotalDays / 365.0);
        double basis = (double)(f.PurchasePrice > 0 ? f.PurchasePrice : a.Economics.PurchasePriceNew);
        double value = basis * Math.Pow(0.94, years) * (0.6 + 0.4 * f.Condition / 100.0) - f.AirframeHours * 5;
        return Math.Round((decimal)Math.Max(value, (double)a.Economics.PurchasePriceNew * 0.25), 0);
    }

    /// <summary>Selling to a dealer yields 90 % of market value.</summary>
    public static decimal SaleValue(AircraftProfile a, FleetAircraft f, DateTime gameTime) =>
        Math.Round(MarketValue(a, f, gameTime) * 0.9m, 0);

    /// <summary>Wear per flight: 0.05 per flight hour, plus damage for hard landings and over-G.</summary>
    public static double ConditionLoss(double flightHours, double touchdownFpm, double maxG)
    {
        double loss = flightHours * 0.05;
        double rate = Math.Abs(touchdownFpm);
        if (rate > 900) loss += 8;
        else if (rate > 600) loss += 2;
        if (maxG > 2.5) loss += 10;
        else if (maxG > 2.0) loss += 3;
        return loss;
    }

    /// <summary>Cost to restore an aircraft to 100 % condition.</summary>
    public static decimal RepairCost(AircraftProfile a, double condition) =>
        Math.Round(a.Economics.PurchasePriceNew * 0.004m * (decimal)Math.Max(0, 100 - condition), 0);

    /// <summary>Monthly insurance only applies to aircraft the player owns or leases; rentals include it.</summary>
    public static decimal MonthlyFixedCost(AircraftProfile a, FleetAircraft f) => f.Ownership switch
    {
        Ownership.Owned => a.Economics.InsurancePerMonth,
        Ownership.Leased => a.Economics.InsurancePerMonth + f.MonthlyRate,
        _ => 0
    };
}
