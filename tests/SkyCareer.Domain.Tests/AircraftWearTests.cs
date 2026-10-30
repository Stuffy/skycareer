using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;

namespace SkyCareer.Domain.Tests;

public class AircraftWearTests
{
    [Fact]
    public void Sim_reading_drops_absent_components_and_survives_a_round_trip()
    {
        var wear = AircraftWear.FromSim(new Dictionary<int, double> { [35] = 0.9775, [39] = 0, [40] = 0.99391, [99] = 0.5 });
        Assert.Equal([35, 40], wear.Levels.Keys.Order());
        var copy = AircraftWear.Parse(wear.Serialize());
        Assert.Equal(wear.Levels, copy.Levels);
        Assert.True(AircraftWear.Parse(null).IsEmpty);
        Assert.True(AircraftWear.Parse("x=1;37=abc;77=0.5").IsEmpty);
    }

    [Fact]
    public void Delta_counts_only_losses_of_components_in_both_readings()
    {
        var start = new AircraftWear(new Dictionary<int, double> { [35] = 0.98, [36] = 0.90, [37] = 0.95 });
        var end = new AircraftWear(new Dictionary<int, double> { [35] = 0.97, [36] = 1.0, [40] = 0.9 });
        var delta = AircraftWear.Delta(start, end);

        Assert.Equal([35, 36], delta.Levels.Keys.Order());
        Assert.Equal(0.01, delta.Levels[35], 6);
        Assert.Equal(0, delta.Levels[36]);   // repaired in the sim: no wear
        Assert.Equal(0.005, delta.AverageLoss, 6);
        Assert.Equal(WearComponents.LandingGear, delta.GroupLosses()[0].Group);
    }

    [Fact]
    public void Own_state_wears_restores_and_drives_the_effective_condition()
    {
        var state = AircraftWear.Empty.Wear(new AircraftWear(new Dictionary<int, double> { [37] = 0.4, [38] = 0.1, [36] = 0 }));
        Assert.Equal(0.6, state.Level(37), 6);
        Assert.Equal(1, state.Level(36));
        Assert.Equal(1, state.Level(5));   // never reported: as new
        Assert.Equal((WearComponents.Tires, 0.6), (state.Groups()[0].Group, Math.Round(state.Groups()[0].Level, 6)));
        Assert.Single(state.Warnings());

        var checkedOnce = state.Restore(0.15);
        Assert.Equal(0.75, checkedOnce.Level(37), 6);
        Assert.Empty(checkedOnce.Warnings());
        Assert.Equal(1, state.Restore(1).WorstLevel);

        var aircraft = new FleetAircraft { ProfileId = "c208b", Registration = "D-TEST", Location = "EDDF", Condition = 90, ComponentWear = state.Serialize() };
        Assert.Equal(60, aircraft.EffectiveCondition, 6);
        aircraft.ComponentWear = null;
        Assert.Equal(90, aircraft.EffectiveCondition);
    }

    [Fact]
    public void Single_repairs_add_up_to_the_full_component_repair()
    {
        var profile = new AircraftProfile
        {
            Id = "t", Name = "Test", IcaoType = "TEST", OperatingEmptyKg = 2000, MaxZeroFuelKg = 4000, MaxTakeoffKg = 4000,
            MaxLandingKg = 4000, MaxFuelKg = 800, Economics = new AircraftEconomics { PurchasePriceNew = 10_000_000m }
        };
        var wear = new AircraftWear(new Dictionary<int, double> { [5] = 0.8, [6] = 0.9, [37] = 0.5, [40] = 0.95 });

        // The four tire components share 0.2 % of the new price: a quarter of 20 000 € at 50 % wear.
        Assert.Equal(2_500m, Finance.FleetEconomics.ComponentRepairCost(profile, wear, [37]));
        decimal singles = wear.Levels.Keys.Sum(id => Finance.FleetEconomics.ComponentRepairCost(profile, wear, [id]));
        Assert.Equal(Finance.FleetEconomics.ComponentRepairCost(profile, wear), singles);

        var repaired = wear.RepairComponents([37, 99]);
        Assert.Equal(1, repaired.Level(37));
        Assert.Equal(0.8, repaired.Level(5));
        Assert.Equal(4, repaired.Levels.Count);

        Assert.Equal(TimeSpan.FromHours(2), WearComponents.RepairDowntime(WearComponents.Tires));
        Assert.Equal(TimeSpan.FromHours(24), WearComponents.RepairDowntime(WearComponents.Engine));
        Assert.Equal(TimeSpan.FromHours(6), WearComponents.RepairDowntime(WearComponents.Controls));
    }
}
