using SkyCareer.Application;
using SkyCareer.Application.Logging;
using SkyCareer.Data;
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
    private FlightJournal? _journal;
    private ConnectionState _lastLoggedState = ConnectionState.Disconnected;

    /// <summary>Saved tracks and the running journals of career flights (next to the database).</summary>
    public static string TracksDirectory => Path.Combine(Path.GetDirectoryName(SkyCareerDb.DefaultPath)!, "tracks");

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
        AttachLog(session);
        lock (_sessionLock)
        {
            CloseJournal(delete: true);
            Session = session;
            ContractId = contractId;
            DepartureIssues = [];
            if (contractId is { } id)
            {
                // A stale journal of an earlier attempt at this job must not be continued.
                OpenJournal(id, fresh: true);
            }
        }
        Log(contractId is null ? "Flight tracking started" : "Flight tracking started for contract");
        Changed?.Invoke();
    }

    /// <summary>
    /// Continues tracking a job that is already flying (after an app restart): replays its journal into a new session
    /// and keeps recording. Without a journal the session is picked up from the current state (times estimated from
    /// <paramref name="startedAt"/>). Returns false when there is neither a journal nor live telemetry.
    /// </summary>
    public bool ResumeFlight(Guid contractId, DateTimeOffset startedAt)
    {
        var history = FlightJournal.Read(TracksDirectory, contractId);
        var current = Latest;
        if (history.Count == 0 && current is null) return false;

        var session = new FlightSession();
        foreach (var t in history) session.Process(t);
        if (history.Count == 0) session.Resume(current!, startedAt);
        AttachLog(session);
        lock (_sessionLock)
        {
            CloseJournal(delete: false);
            Session = session;
            ContractId = contractId;
            OpenJournal(contractId, fresh: false);
        }
        Log(history.Count > 0
            ? $"Flight tracking resumed from journal ({history.Count} samples, phase {session.Phases.Phase})"
            : $"Flight tracking resumed without journal (phase {session.Phases.Phase}, times estimated)");
        Changed?.Invoke();
        return true;
    }

    public void EndFlight()
    {
        lock (_sessionLock)
        {
            CloseJournal(delete: true);
            Session = null;
            ContractId = null;
        }
        Changed?.Invoke();
    }

    private void AttachLog(FlightSession session)
    {
        session.Phases.PhaseChanged += (_, c) => Log($"Phase {c.From} → {c.To}");
        session.Phases.TouchedDown += (_, t) => Log($"Touchdown {t.VerticalSpeedFpm:N0} fpm, {t.GForce:N2} G");
        session.Integrity.ViolationDetected += (_, v) => Log($"⚠ {v.Kind}: {v.Detail}");
    }

    /// <summary>Starts (or continues) recording; tracking goes on without a journal if the file can't be written.</summary>
    private void OpenJournal(Guid contractId, bool fresh)
    {
        try
        {
            Directory.CreateDirectory(TracksDirectory);
            if (fresh) File.Delete(FlightJournal.PathFor(TracksDirectory, contractId));
            _journal = new FlightJournal(TracksDirectory, contractId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Flug", "Flugaufzeichnung konnte nicht angelegt werden – nach einem Neustart wird der Flug geschätzt fortgesetzt", ex);
            _journal = null;
        }
    }

    private void CloseJournal(bool delete)
    {
        if (delete) _journal?.Delete();
        else _journal?.Dispose();
        _journal = null;
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

        Directory.CreateDirectory(TracksDirectory);
        var path = Path.Combine(TracksDirectory, $"{name}.jsonl");
        await using var writer = new StreamWriter(path);
        foreach (var t in samples) await FlightLogFile.AppendAsync(writer, t);
        return path;
    }

    private async Task StartAsync(IFlightDataSource source, string name)
    {
        await StopAsync();
        _source = source;
        SourceName = name;
        _source.StateChanged += (_, s) =>
        {
            // While MSFS isn't running the source retries every few seconds; only real connects/disconnects are worth a line.
            if (s == ConnectionState.Connected || _lastLoggedState == ConnectionState.Connected) Log($"Connection: {s}");
            _lastLoggedState = s;
            Changed?.Invoke();
        };
        _source.TelemetryReceived += OnTelemetry;
        await _source.StartAsync();
        Changed?.Invoke();
    }

    private void OnTelemetry(object? sender, Telemetry t)
    {
        Latest = t;
        lock (_sessionLock)
        {
            Session?.Process(t);
            try
            {
                _journal?.Append(t);
            }
            catch (IOException ex)
            {
                // Losing the journal only matters after a crash; keep tracking.
                AppLog.Warn("Flug", "Flugaufzeichnung konnte nicht geschrieben werden", ex);
                CloseJournal(delete: false);
            }
        }
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
        AppLog.Info("Flug", message);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        // Keep the journal on exit: the next start resumes the flight from it.
        lock (_sessionLock) CloseJournal(delete: false);
    }
}
