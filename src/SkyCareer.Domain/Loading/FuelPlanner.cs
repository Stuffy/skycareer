using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Domain.Loading;

public enum FlightRules { Ifr, Vfr }

public sealed record FuelPlan(Mass Taxi, Mass Trip, Mass Contingency, Mass Alternate, Mass FinalReserve, Mass Extra)
{
    public Mass Block => Taxi + Trip + Contingency + Alternate + FinalReserve + Extra;
    public Mass Takeoff => Block - Taxi;
    /// <summary>Minimum fuel legally required at takeoff (everything except discretionary extra).</summary>
    public Mass MinimumTakeoff => Trip + Contingency + Alternate + FinalReserve;
}

/// <summary>
/// Simple EASA-style fuel planning for flights without a SimBrief OFP:
/// IFR = trip + 5 % contingency + alternate + 30 min (turbine) / 45 min (piston) final reserve;
/// VFR = trip + 30 min reserve (45 min at night).
/// </summary>
public static class FuelPlanner
{
    public static FuelPlan Plan(AircraftProfile aircraft, double distanceNm, FlightRules rules,
        double alternateDistanceNm = 0, bool night = false, double extraMinutes = 0)
    {
        if (aircraft.CruiseKtas <= 0 || aircraft.FuelBurnKgPerHour <= 0)
            throw new InvalidOperationException($"Aircraft profile '{aircraft.Id}' has no cruise performance data.");

        double burnPerMin = aircraft.FuelBurnKgPerHour / 60.0;
        // Climb/descent and routing inefficiency: add a fixed 10 minutes plus 5 % distance.
        double tripMinutes = distanceNm * 1.05 / aircraft.CruiseKtas * 60.0 + 10;

        var trip = Mass.FromKg(tripMinutes * burnPerMin);
        var taxi = Mass.FromKg(aircraft.TaxiFuelKg);
        var extra = Mass.FromKg(extraMinutes * burnPerMin);

        if (rules == FlightRules.Vfr)
        {
            var reserve = Mass.FromKg((night ? 45 : 30) * burnPerMin);
            return new FuelPlan(taxi, trip, Mass.Zero, Mass.Zero, reserve, extra);
        }

        var contingency = Mass.FromKg(Math.Max(trip.Kg * 0.05, 5 * burnPerMin));
        double altMinutes = alternateDistanceNm > 0 ? alternateDistanceNm / aircraft.CruiseKtas * 60.0 + 5 : 0;
        var alternate = Mass.FromKg(altMinutes * burnPerMin);
        int finalReserveMinutes = aircraft.FuelType == FuelType.AvGas ? 45 : 30;
        var finalReserve = Mass.FromKg(finalReserveMinutes * burnPerMin);

        return new FuelPlan(taxi, trip, contingency, alternate, finalReserve, extra);
    }
}
