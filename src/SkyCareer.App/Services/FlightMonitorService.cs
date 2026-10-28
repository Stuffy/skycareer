using SkyCareer.Application;
using SkyCareer.Sim;
using SkyCareer.Sim.Replay;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.App.Services;

/// <summary>
/// App-wide owner of the data source and the current flight session. UI components subscribe to
/// <see cref="Changed"/> and re-render; they never talk to SimConnect directly.
/// </summary>
public sealed class FlightMonitorService : IFlightSessionHost, IAsyncDisposable
{
    private readonly Lock _sessionLock = new();
    private IFlightDataSource? _source;

    public ConnectionState Connection => _source?.State ?? ConnectionState.Disconnected;
    public bool Connected => Connection == ConnectionState.Connected;
    public bool HasSession
    {
        get { lock (_sessionLock) return Session is not null; }
    }
    public IReadOnlyList<string> DepartureIssues { get; set; } = [];
    public string SourceName { get; private set; } = "none";
    public Telemetry? Latest { get; private set; }
    public FlightSession? Session { get; private set; }
    public List<string> EventLog { get; } = [];

    public event Action? Changed;

    public bool LiveAvailable => FlightDataSourceFactory.SimConnectAvailable;

    /// <summary>Payload/fuel write access; null unless connected to the live sim.</summary>
    public IAircraftLoader? Loader => Connection == ConnectionState.Connected ? _source as IAircraftLoader : null;
    public IAircraftInspector? Inspector => Connection == ConnectionState.Connected ? _source as IAircraftInspector : null;
    public IAirportFacilityReader? Facilities => Connection == ConnectionState.Connected ? _source as IAirportFacilityReader : null;

    public Task ConnectLiveAsync() =>
        StartAsync(FlightDataSourceFactory.CreateLive()
            ?? throw new InvalidOperationException("This build has no SimConnect support. Install the MSFS 2024 SDK and rebuild."), "MSFS 2024");

    public Task StartReplayAsync(string path, double speed) => StartAsync(ReplaySource.FromFile(path, speed), Path.GetFileName(path));

    /// <summary>Contract the current session belongs to; null for free/test tracking.</summary>
    public Guid? ContractId { get; private set; }

    /// <summary>Begins tracking a new flight (armed automatically by the departure coordinator, or a test flight).</summary>
    public void BeginFlight(Guid? contractId = null)
    {
        var session = new FlightSession();
        session.Phases.PhaseChanged += (_, c) => Log($"Phase {c.From} → {c.To}");
        session.Phases.TouchedDown += (_, t) => Log($"Touchdown {t.VerticalSpeedFpm:N0} fpm, {t.GForce:N2} G");
        session.Integrity.ViolationDetected += (_, v) => Log($"⚠ {v.Kind}: {v.Detail}");
        lock (_sessionLock)
        {
            Session = session;
            ContractId = contractId;
            DepartureIssues = [];
        }
        Log(contractId is null ? "Flight tracking started" : "Flight tracking started for contract");
        Changed?.Invoke();
    }

    public void EndFlight()
    {
        lock (_sessionLock)
        {
            Session = null;
            ContractId = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Thread-safe summary: telemetry is processed on the SimConnect thread while the UI reads.</summary>
    public FlightSummary? Summarize()
    {
        lock (_sessionLock) return Session?.Summarize();
    }

    public FlightPhase? Phase
    {
        get { lock (_sessionLock) return Session?.Phases.Phase; }
    }

    /// <summary>Writes the recorded samples as JSON Lines (replayable) and returns the file path.</summary>
    public async Task<string?> SaveTrackAsync(string name)
    {
        Telemetry[] samples;
        lock (_sessionLock) samples = Session?.Samples.ToArray() ?? [];
        if (samples.Length == 0) return null;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyCareer", "tracks");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}.jsonl");
        await using var writer = new StreamWriter(path);
        foreach (var t in samples) await FlightLogFile.AppendAsync(writer, t);
        return path;
    }

    private async Task StartAsync(IFlightDataSource source, string name)
    {
        await StopAsync();
        _source = source;
        SourceName = name;
        _source.StateChanged += (_, s) => { Log($"Connection: {s}"); Changed?.Invoke(); };
        _source.TelemetryReceived += OnTelemetry;
        await _source.StartAsync();
        Changed?.Invoke();
    }

    private void OnTelemetry(object? sender, Telemetry t)
    {
        Latest = t;
        lock (_sessionLock) Session?.Process(t);
        Changed?.Invoke();
    }

    public async Task StopAsync()
    {
        if (_source is null) return;
        _source.TelemetryReceived -= OnTelemetry;
        await _source.DisposeAsync().ConfigureAwait(false);
        _source = null;
        Changed?.Invoke();
    }

    public void Log(string message)
    {
        lock (EventLog) EventLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
