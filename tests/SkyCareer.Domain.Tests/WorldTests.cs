using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.World;

namespace SkyCareer.Domain.Tests;

public class WorldTests
{
    [Fact]
    public void Sun_elevation_tells_day_from_night()
    {
        // Equinox (day 80), equator, Greenwich: overhead at noon, straight below at midnight.
        Assert.InRange(SolarPosition.ElevationDeg(new GeoPosition(0, 0), 80, 12 * 3600), 85, 90);
        Assert.InRange(SolarPosition.ElevationDeg(new GeoPosition(0, 0), 80, 0), -90, -85);
        // 12:00 UTC is midnight at the date line.
        Assert.True(SolarPosition.IsNight(new GeoPosition(0, 180), 80, 12 * 3600));
        Assert.False(SolarPosition.IsNight(new GeoPosition(0, 0), 80, 12 * 3600));
        // Frankfurt in midsummer: night at 00:00 UTC, still light at 19:30 UTC (sunset is about 19:40).
        var frankfurt = new GeoPosition(50.03, 8.57);
        Assert.True(SolarPosition.IsNight(frankfurt, 172, 0));
        Assert.False(SolarPosition.IsNight(frankfurt, 172, 19.5 * 3600));
        // Midnight sun on Svalbard in June, polar night in December.
        var svalbard = new GeoPosition(78.2, 15.6);
        Assert.False(SolarPosition.IsNight(svalbard, 172, 23 * 3600));
        Assert.True(SolarPosition.IsNight(svalbard, 355, 11 * 3600));
    }

    [Fact]
    public void Crosswind_is_the_wind_component_across_the_heading()
    {
        Assert.Equal(20, Navigation.CrosswindKt(270, 20, 360), 6);
        Assert.Equal(20, Navigation.CrosswindKt(90, 20, 0), 6);
        Assert.Equal(0, Navigation.CrosswindKt(360, 20, 0), 6);
        Assert.Equal(0, Navigation.CrosswindKt(180, 20, 0), 6);
        Assert.Equal(14.142, Navigation.CrosswindKt(45, 20, 0), 3);
    }

    [Fact]
    public void Fuel_prices_follow_the_region()
    {
        static Airport At(string ident, string country, AirportSize size = AirportSize.Large) =>
            new(ident, ident, new GeoPosition(0, 0), size, 3000, [RunwaySurface.Paved], country);

        Assert.Equal(1.18m, CostModel.FuelRegionFactor(At("EDDF", "DE")));
        Assert.Equal(0.75m, CostModel.FuelRegionFactor(At("OERK", "SA")));
        Assert.Equal(1m, CostModel.FuelRegionFactor(At("XXXX", "")));
        // Countries without an entry get a stable level of their own.
        decimal other = CostModel.FuelRegionFactor(At("FAOR", "ZA"));
        Assert.InRange(other, 0.90m, 1.15m);
        Assert.Equal(other, CostModel.FuelRegionFactor(At("FACT", "ZA")));
        // A strip without an ICAO code has its fuel brought in.
        Assert.Equal(1.00m * CostModel.RemoteStripFuelFactor, CostModel.FuelRegionFactor(At("BR-2197", "BR", AirportSize.Small)));

        var tables = EconomyTables.For(Career.Difficulty.Normal);
        var day = new DateTime(2026, 10, 15);
        decimal riyadh = CostModel.FuelPricePerKg(tables, FuelType.JetA, At("SAME", "SA"), day);
        decimal zurich = CostModel.FuelPricePerKg(tables, FuelType.JetA, At("SAME", "CH"), day);
        Assert.Equal(1.28 / 0.75, (double)(zurich / riyadh), 2);
    }
}
