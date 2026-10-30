using SkyCareer.Sim.Tracking;

namespace SkyCareer.Sim.Tests;

public class DeboardingTests
{
    [Fact]
    public void Unloading_after_parking_is_not_a_payload_violation()
    {
        var session = new FlightSession();
        foreach (var t in SyntheticFlight.Standard().Samples) session.Process(t);
        Assert.Equal(FlightPhase.Arrived, session.Phases.Phase);

        var last = session.Samples[^1];
        session.Process(last with { Timestamp = last.Timestamp.AddSeconds(30), PayloadKg = 0 });

        Assert.DoesNotContain(session.Integrity.Violations, v => v.Kind == ViolationKind.PayloadChanged);
    }

    [Fact]
    public void Unloading_while_taxiing_in_is_not_a_payload_violation()
    {
        var session = new FlightSession();
        var samples = SyntheticFlight.Standard().Samples;
        foreach (var t in samples.Take(samples.Count - 30)) session.Process(t);
        Assert.Equal(FlightPhase.TaxiIn, session.Phases.Phase);

        var last = session.Samples[^1];
        session.Process(last with { Timestamp = last.Timestamp.AddSeconds(1), PayloadKg = 100 });
        Assert.Empty(session.Integrity.Violations);
    }

    [Fact]
    public void After_a_restart_gap_a_landing_and_a_new_payload_are_not_judged()
    {
        // The app was closed in cruise; the journal ends there. When it comes back the aircraft is parked and deboarded.
        var samples = SyntheticFlight.Standard().Samples;
        var journal = samples.Take(700).ToList();
        var session = new FlightSession();
        foreach (var t in journal) session.Process(t);
        Assert.Equal(FlightPhase.Cruise, session.Phases.Phase);
        session.ContinueAfterRestart(journal[0].Timestamp);

        var back = samples[^1] with { Timestamp = journal[^1].Timestamp.AddMinutes(20), PayloadKg = 120 };
        session.Process(back);
        session.Process(back with { Timestamp = back.Timestamp.AddSeconds(1) });

        Assert.Equal(FlightPhase.Arrived, session.Phases.Phase);
        Assert.Empty(session.Phases.Touchdowns);
        Assert.Empty(session.Integrity.Violations);
        var summary = session.Summarize();
        Assert.Contains(summary.Notes!, n => n.Contains("Landerate nicht erfasst"));
        Assert.NotNull(summary.LandingTime);
    }

    [Fact]
    public void After_a_restart_gap_in_flight_the_payload_lock_follows_and_later_changes_count()
    {
        var samples = SyntheticFlight.Standard().Samples;
        var session = new FlightSession();
        foreach (var t in samples.Take(700)) session.Process(t);
        session.ContinueAfterRestart(samples[0].Timestamp);

        var back = samples[760] with { Timestamp = samples[760].Timestamp.AddMinutes(5), PayloadKg = 500 };
        session.Process(back);
        Assert.Empty(session.Integrity.Violations);
        Assert.Contains(session.Notes, n => n.Contains("700 → 500 kg"));

        session.Process(back with { Timestamp = back.Timestamp.AddSeconds(1), PayloadKg = 300 });
        Assert.Contains(session.Integrity.Violations, v => v.Kind == ViolationKind.PayloadChanged);
    }
}
