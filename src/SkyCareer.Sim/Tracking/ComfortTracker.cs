using SkyCareer.Domain.Career;

namespace SkyCareer.Sim.Tracking;

/// <summary>Accumulates <see cref="ComfortStats"/> from the airborne samples of a flight.</summary>
public sealed class ComfortTracker
{
    private readonly SustainedPeak _bank = new(TimeSpan.FromSeconds(3));
    private readonly SustainedPeak _sink = new(TimeSpan.FromSeconds(5));
    private readonly SustainedPeak _g = new(TimeSpan.FromSeconds(1));
    private double _airborne, _maxBank, _steepBank, _maxSink, _highSink, _maxClimb, _gExcursion;

    /// <param name="t">An airborne sample.</param>
    /// <param name="dt">Seconds since the previous sample.</param>
    public void Add(Telemetry t, double dt)
    {
        _airborne += dt;
        double bank = Math.Abs(t.BankDeg ?? 0);
        double sink = Math.Max(0, -t.VerticalSpeedFpm);
        _maxBank = Math.Max(_maxBank, bank);
        _maxSink = Math.Max(_maxSink, sink);
        _maxClimb = Math.Max(_maxClimb, t.VerticalSpeedFpm);
        if (bank > ComfortStats.SteepBankDeg) _steepBank += dt;
        if (sink > ComfortStats.HighSinkFpm) _highSink += dt;
        if (t.GForce is > ComfortStats.MaxComfortG or < ComfortStats.MinComfortG) _gExcursion += dt;
        _bank.Add(t.Timestamp, bank);
        _sink.Add(t.Timestamp, sink);
        _g.Add(t.Timestamp, t.GForce);
    }

    /// <summary>Null until the aircraft has been in the air.</summary>
    public ComfortStats? Stats => _airborne <= 0 ? null : new ComfortStats
    {
        AirborneSeconds = _airborne,
        MaxBankDeg = _maxBank, SustainedBankDeg = _bank.Peak, SteepBankSeconds = _steepBank,
        MaxSinkFpm = _maxSink, SustainedSinkFpm = _sink.Peak, HighSinkSeconds = _highSink,
        MaxClimbFpm = _maxClimb,
        SustainedG = Math.Max(1, _g.Peak), GExcursionSeconds = _gExcursion
    };

    /// <summary>The highest value that was held for a whole window: the maximum over time of the window's minimum.</summary>
    private sealed class SustainedPeak(TimeSpan window)
    {
        private readonly Queue<(DateTimeOffset Time, double Value)> _values = new();
        public double Peak { get; private set; }

        public void Add(DateTimeOffset time, double value)
        {
            // A gap in the samples (pause, restart) starts a new window.
            if (_values.Count > 0 && time - _values.Last().Time > window) _values.Clear();
            _values.Enqueue((time, value));
            // Keep one sample at or before the window's start so the window is fully covered.
            while (_values.Count > 1 && time - _values.ElementAt(1).Time >= window) _values.Dequeue();
            if (time - _values.Peek().Time >= window) Peak = Math.Max(Peak, _values.Min(v => v.Value));
        }
    }
}
