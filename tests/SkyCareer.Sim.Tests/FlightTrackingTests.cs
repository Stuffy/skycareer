using SkyCareer.Sim.Replay;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Sim.Tests;

public class FlightTrackingTests
{
    private static FlightSession Run(IEnumerable<Telemetry> samples)
    {
        var session = new FlightSession();
        foreach (var t in samples) session.Process(t);
        return session;
    }

    [Fact]
    public void Standard_flight_passes_through_all_phases()
    {
        var phases = new List<FlightPhase>();
        var session = new FlightSession();
        session.Phases.PhaseChanged += (_, c) => phases.Add(c.To);
        foreach (var t in SyntheticFlight.Standard().Samples) session.Process(t);

        Assert.Equal(
            [FlightPhase.TaxiOut, FlightPhase.TakeoffRoll, FlightPhase.Climb, FlightPhase.Cruise, FlightPhase.Descent,
             FlightPhase.Approach, FlightPhase.Rollout, FlightPhase.TaxiIn, FlightPhase.Arrived],
            phases);

        var summary = session.Summarize();
        Assert.True(session.IsFinished);
        Assert.Empty(summary.Violations);
        Assert.NotNull(summary.BlockOff);
        Assert.NotNull(summary.BlockOn);
        Assert.True(summary.AirTime > TimeSpan.FromMinutes(18));
        Assert.True(summary.FuelUsedKg > 0);
        Assert.InRange(summary.DistanceFlownNm, 35, 60);
    }

    [Fact]
    public void Touchdown_reports_rate_and_peak_g()
    {
        var session = Run(SyntheticFlight.Standard(touchdownFpm: -420, touchdownG: 1.65).Samples);

        var td = Assert.Single(session.Phases.Touchdowns);
        Assert.Equal(-420, td.VerticalSpeedFpm);
        Assert.Equal(1.65, td.GForce, 3);
        Assert.Equal(0, td.Bounces);
    }

    [Fact]
    public void Short_hop_after_touchdown_counts_as_bounce()
    {
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(10, t => t with { GroundSpeedKt = 60 })
            .Hold(120, t => t with { OnGround = false, GroundSpeedKt = 90, VerticalSpeedFpm = 500 })
            .Hold(1, t => t with { OnGround = true, VerticalSpeedFpm = 0, TouchdownVsFpm = -500, GroundSpeedKt = 65 })
            .Hold(2, t => t with { OnGround = false })
            .Hold(1, t => t with { OnGround = true, GForce = 1.9 })
            .Hold(10, t => t with { GForce = 1, GroundSpeedKt = 20 });

        var td = Assert.Single(Run(flight.Samples).Phases.Touchdowns);
        Assert.Equal(1, td.Bounces);
        Assert.Equal(1.9, td.GForce, 3);
    }

    [Fact]
    public void Rejected_takeoff_returns_to_taxi()
    {
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(5, t => t with { GroundSpeedKt = 50 })
            .Hold(5, t => t with { GroundSpeedKt = 8 });

        Assert.Equal(FlightPhase.TaxiOut, Run(flight.Samples).Phases.Phase);
    }

    [Fact]
    public void Inflight_refuel_and_slew_are_flagged_once()
    {
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(10, t => t with { GroundSpeedKt = 60 })
            .Hold(60, t => t with { OnGround = false, GroundSpeedKt = 120, VerticalSpeedFpm = 500 })
            .Hold(1, t => t with { FuelKg = t.FuelKg + 300 })
            .Hold(30, t => t with { Slew = true })
            .Hold(10, t => t with { Slew = false });

        var kinds = Run(flight.Samples).Integrity.Violations.Select(v => v.Kind).ToList();
        Assert.Equal([ViolationKind.InflightRefuel, ViolationKind.Slew], kinds);
    }

    [Fact]
    public void Payload_may_change_while_taxiing_but_not_after_the_takeoff_roll_started()
    {
        var taxi = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(5, t => t with { PayloadKg = t.PayloadKg + 400 });
        Assert.DoesNotContain(Run(taxi.Samples).Integrity.Violations, v => v.Kind == ViolationKind.PayloadChanged);

        var roll = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(3, t => t with { GroundSpeedKt = 60 })
            .Hold(3, t => t with { PayloadKg = t.PayloadKg + 400 });
        Assert.Contains(Run(roll.Samples).Integrity.Violations, v => v.Kind == ViolationKind.PayloadChanged);
    }

    [Fact]
    public void Rejected_takeoff_releases_the_payload_lock()
    {
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true, ParkingBrake = false, GroundSpeedKt = 15 })
            .Hold(3, t => t with { GroundSpeedKt = 60 })
            .Hold(5, t => t with { GroundSpeedKt = 10 })
            .Hold(5, t => t with { PayloadKg = t.PayloadKg - 200 });
        Assert.DoesNotContain(Run(flight.Samples).Integrity.Violations, v => v.Kind == ViolationKind.PayloadChanged);
    }

    [Fact]
    public void Position_jump_is_flagged_as_teleport()
    {
        var flight = new SyntheticFlight()
            .Hold(5, t => t with { AnyEngineRunning = true })
            .Hold(1, t => t with { Latitude = t.Latitude + 1 });

        Assert.Contains(Run(flight.Samples).Integrity.Violations, v => v.Kind == ViolationKind.Teleport);
    }

    [Fact]
    public async Task Recorded_log_replays_identically()
    {
        var samples = SyntheticFlight.Standard().Samples;
        var path = Path.Combine(Path.GetTempPath(), $"skycareer-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using (var writer = new StreamWriter(path))
                foreach (var t in samples) await FlightLogFile.AppendAsync(writer, t);

            var session = new FlightSession();
            await using var replay = ReplaySource.FromFile(path);
            replay.TelemetryReceived += (_, t) => session.Process(t);
            await replay.StartAsync();
            await replay.Completion;

            Assert.Equal(samples.Count, session.Samples.Count);
            Assert.Equal(FlightPhase.Arrived, session.Phases.Phase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
