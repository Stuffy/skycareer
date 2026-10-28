namespace SkyCareer.Sim.Replay;

/// <summary>
/// Plays recorded telemetry back as if it came from the simulator. Used for development and tests
/// without MSFS running. <paramref name="speed"/> = 0 emits as fast as possible.
/// </summary>
public sealed class ReplaySource(IAsyncEnumerable<Telemetry> samples, double speed = 0) : IFlightDataSource
{
    private CancellationTokenSource? _cts;
    private Task? _run;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<Telemetry>? TelemetryReceived;

    /// <summary>Completes when all samples have been emitted.</summary>
    public Task Completion => _run ?? Task.CompletedTask;

    public static ReplaySource FromFile(string path, double speed = 0) => new(FlightLogFile.ReadAsync(path), speed);

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(ConnectionState.Connected);
        _run = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        DateTimeOffset? previous = null;
        await foreach (var t in samples.WithCancellation(ct))
        {
            if (speed > 0 && previous is { } p)
            {
                var wait = (t.Timestamp - p) / speed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
            previous = t.Timestamp;
            TelemetryReceived?.Invoke(this, t);
        }
        SetState(ConnectionState.Disconnected);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        try { if (_run is not null) await _run; } catch (OperationCanceledException) { }
        SetState(ConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    private void SetState(ConnectionState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(this, s);
    }
}
