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
}
