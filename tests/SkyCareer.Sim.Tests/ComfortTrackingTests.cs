using SkyCareer.Sim.Tracking;

namespace SkyCareer.Sim.Tests;

public class ComfortTrackingTests
{
    private static FlightSession Run(IEnumerable<Telemetry> samples)
    {
        var session = new FlightSession();
        foreach (var t in samples) session.Process(t);
        return session;
    }

    [Fact]
    public void A_standard_flight_is_comfortable()
    {
        var stats = Run(SyntheticFlight.Standard().Samples).Summarize().Comfort!;

        Assert.InRange(stats.AirborneSeconds, 1100, 1200);
        Assert.Equal(800, stats.MaxSinkFpm);
        Assert.Equal(800, stats.SustainedSinkFpm);
        Assert.Equal(800, stats.MaxClimbFpm);
        Assert.Equal(0, stats.MaxBankDeg);
        Assert.Equal(0, stats.GExcursionSeconds);
        Assert.Equal(1, stats.SustainedG, 3);
    }

    [Fact]
    public void Steep_turns_fast_descents_and_g_are_recorded_in_the_air_only()
    {
        var flight = new SyntheticFlight()
            // Banking on the ground (a taxi turn) doesn't count.
            .Hold(20, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15, BankDeg = 45 })
            .Hold(20, t => t with { GroundSpeedKt = 60, BankDeg = 0 })
            .Hold(120, t => t with { OnGround = false, GroundSpeedKt = 110, VerticalSpeedFpm = 800 })
            .Hold(20, t => t with { VerticalSpeedFpm = 0, BankDeg = -40 })
            .Hold(2, t => t with { BankDeg = 60, GForce = 2.0 })
            .Hold(30, t => t with { BankDeg = 5, GForce = 1 })
            .Hold(10, t => t with { VerticalSpeedFpm = -3500 })
            .Hold(1, t => t with { VerticalSpeedFpm = -6000 })
            .Hold(60, t => t with { VerticalSpeedFpm = -700 });

        var stats = Run(flight.Samples).Summarize().Comfort!;

        Assert.Equal(60, stats.MaxBankDeg);
        Assert.Equal(40, stats.SustainedBankDeg);      // the 2 s at 60° are a spike, not a sustained bank
        Assert.Equal(22, stats.SteepBankSeconds);
        Assert.Equal(6000, stats.MaxSinkFpm);
        Assert.Equal(3500, stats.SustainedSinkFpm);
        Assert.Equal(11, stats.HighSinkSeconds);
        Assert.Equal(2, stats.GExcursionSeconds);
        Assert.Equal(2.0, stats.SustainedG, 3);
    }

    [Fact]
    public void A_flight_that_never_left_the_ground_has_no_comfort_data()
    {
        var taxi = new SyntheticFlight().Hold(60, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 });
        Assert.Null(Run(taxi.Samples).Summarize().Comfort);
    }

    [Fact]
    public void Touchdown_notes_night_visibility_and_crosswind()
    {
        // Midsummer, 00:00 UTC at 50° N / 8° E: local solar midnight, the sun about 16° below the horizon.
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(10, t => t with { GroundSpeedKt = 60 })
            .Hold(120, t => t with { OnGround = false, GroundSpeedKt = 90, VerticalSpeedFpm = 500 })
            .Hold(1, t => t with
            {
                OnGround = true, VerticalSpeedFpm = 0, TouchdownVsFpm = -200, GroundSpeedKt = 65,
                HeadingTrueDeg = 360, WindDirectionDeg = 270, WindSpeedKt = 20, VisibilityM = 3000, ZuluSeconds = 0, ZuluDayOfYear = 172
            })
            .Hold(10, t => t with { GroundSpeedKt = 20 });

        var conditions = Assert.Single(Run(flight.Samples).Phases.Touchdowns).Conditions!;
        Assert.True(conditions.Night);
        Assert.Equal(3000, conditions.VisibilityM);
        Assert.Equal(20, conditions.CrosswindKt!.Value, 3);

        // Old recordings have none of this: nothing is known, nothing is paid.
        var old = Assert.Single(Run(SyntheticFlight.Standard().Samples).Phases.Touchdowns).Conditions!;
        Assert.Null(old.Night);
        Assert.Null(old.VisibilityM);
        Assert.Null(old.CrosswindKt);
    }
}
