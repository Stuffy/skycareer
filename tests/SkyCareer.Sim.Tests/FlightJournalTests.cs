using SkyCareer.Sim.Replay;

namespace SkyCareer.Sim.Tests;

public class FlightJournalTests
{
    [Fact]
    public void Journal_round_trips_and_survives_a_torn_last_line()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"skycareer-journal-{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var t0 = new Telemetry { Timestamp = DateTimeOffset.UnixEpoch, Latitude = 50, Longitude = 8, OnGround = true };
        try
        {
            using (var journal = new FlightJournal(dir, id))
            {
                journal.Append(t0);
                journal.Append(t0 with { Timestamp = t0.Timestamp.AddSeconds(1), GroundSpeedKt = 10 });
            }
            // A crash while writing leaves half a line behind.
            File.AppendAllText(FlightJournal.PathFor(dir, id), "{\"timestamp\":\"2026-");

            var read = FlightJournal.Read(dir, id);
            Assert.Equal(2, read.Count);
            Assert.Equal(10, read[1].GroundSpeedKt);
            Assert.Empty(FlightJournal.Read(dir, Guid.NewGuid()));

            new FlightJournal(dir, id).Delete();
            Assert.False(File.Exists(FlightJournal.PathFor(dir, id)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resume_without_history_picks_the_phase_from_the_current_state()
    {
        var started = DateTimeOffset.UnixEpoch;
        var now = new Telemetry { Timestamp = started.AddHours(1), OnGround = false, AltitudeAglFt = 8000, GroundSpeedKt = 250, PayloadKg = 500 };

        var airborne = new Sim.Tracking.FlightSession();
        airborne.Resume(now, started);
        Assert.Equal(Sim.Tracking.FlightPhase.Cruise, airborne.Phases.Phase);
        Assert.Equal(started, airborne.Summarize().TakeoffTime);
        Assert.True(airborne.Summarize().Resumed);

        var parked = new Sim.Tracking.FlightSession();
        parked.Resume(now with { OnGround = true, AltitudeAglFt = 0, GroundSpeedKt = 0, ParkingBrake = true }, started);
        Assert.Equal(Sim.Tracking.FlightPhase.Arrived, parked.Phases.Phase);
    }
}
