using SkyCareer.Application.Logging;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Departure;
using SkyCareer.Missions.Settlement;
using SkyCareer.Sim;
using SkyCareer.Sim.Tracking;
using SkyCareer.Localization;

namespace SkyCareer.Application;

/// <summary>What <see cref="FlightAutomation"/> needs from the app's flight monitor (implemented by FlightMonitorService).</summary>
public interface IFlightSessionHost
{
    bool Connected { get; }
    Telemetry? Latest { get; }
    FlightPhase? Phase { get; }
    Guid? ContractId { get; }
    bool HasSession { get; }
    /// <summary>Red departure checks recorded at liftoff; cleared when a new flight begins.</summary>
    IReadOnlyList<string> DepartureIssues { get; set; }
    void BeginFlight(Guid? contractId = null);
    /// <summary>
    /// Continues tracking a job that is already flying (journal replay, or picked up from the current state). With
    /// <paramref name="live"/> false only the journal counts and live telemetry is ignored (another aircraft is loaded).
    /// </summary>
    bool ResumeFlight(Guid contractId, DateTimeOffset startedAt, double? fuelAtBlockOffKg = null, bool live = true);
    /// <summary>The last journal sample of a job and the phase its replay ends in; null without a journal.</summary>
    JournalEnd? ReadJournalEnd(Guid contractId);
    void EndFlight();
    FlightSummary? Summarize();
    Task<string?> SaveTrackAsync(string name);
    /// <summary>The sim's wear-and-tear components (only those the aircraft has); null without sim data.</summary>
    Task<AircraftWear?> ReadWearAsync();
    void Log(string message);
}

/// <summary>Where a job's flight journal ends (see <see cref="IFlightSessionHost.ReadJournalEnd"/>).</summary>
public sealed record JournalEnd(Telemetry Last, FlightPhase Phase);

/// <summary>Ground services (GSX deboarding) at the destination, as far as the settlement waits for them.</summary>
public enum GroundServiceState { NotUsed, NotStarted, InProgress, Completed }

/// <summary>
/// Runs a career flight without a start button: arms tracking when the aircraft sits at the departure airport of an
/// accepted job, keeps the job editable on the ground, starts it at liftoff (unless position or type are wrong) and
/// settles it once the aircraft is parked again (with GSX in use: once deboarding is done). Also evaluates the departure traffic light for every accepted job.
/// With several aircraft on jobs, the job of the aircraft type loaded in the sim (profile sim match) is armed or resumed.
/// Not thread-safe by itself: the caller serialises <see cref="TickAsync"/>.
/// </summary>
public sealed class FlightAutomation(CareerService career, IFlightSessionHost host)
{
    /// <summary>An armed job is dropped when the aircraft shows up this far from its departure airport before takeoff.</summary>
    public const double DisarmDistanceNm = 5;
    /// <summary>Settle anyway when GSX deboarding hasn't started this long after parking.</summary>
    public static readonly TimeSpan DeboardingStartTimeout = TimeSpan.FromMinutes(20);
    /// <summary>Upper bound for a deboarding that never completes.</summary>
    public static readonly TimeSpan DeboardingTimeout = TimeSpan.FromMinutes(60);
    /// <summary>
    /// Pause after GSX deboarding so its receipts are written and booked before the settlement: GSX writes the last
    /// ones (e.g. the crew bus) only minutes after deboarding; 15 s settled flights without them (2026-11-02).
    /// </summary>
    public static readonly TimeSpan ReceiptGrace = TimeSpan.FromMinutes(5);
    /// <summary>A failed read of the sim's wear is retried after this long.</summary>
    public static readonly TimeSpan WearRetry = TimeSpan.FromSeconds(30);

    private List<MissionContract> _jobs = [];
    private Dictionary<Guid, FleetAircraft> _fleet = [];
    private Dictionary<string, Airport> _airports = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty = true;
    private DateTime _gameTime;
    private DepartureReadiness? _groundReadiness;

    public IReadOnlyDictionary<Guid, DepartureReadiness> Readiness { get; private set; } = new Dictionary<Guid, DepartureReadiness>();
    /// <summary>Why a job didn't start at the last liftoff (wrong airport or aircraft).</summary>
    public IReadOnlyDictionary<Guid, string> NotStarted => _notStarted;
    private readonly Dictionary<Guid, string> _notStarted = [];
    /// <summary>When the start wear was last tried per job, so a failing read is retried but not every tick.</summary>
    private readonly Dictionary<Guid, DateTime> _wearTried = [];
    private readonly HashSet<Guid> _wearFromResume = [];
    /// <summary>The sim's wear read once the job's aircraft is parked, before ending the flight in MSFS can reset it.</summary>
    private readonly Dictionary<Guid, AircraftWear> _wearAtArrival = [];
    /// <summary>Jobs settled from their journal alone (another aircraft is loaded): no GSX wait, no sim wear.</summary>
    private readonly HashSet<Guid> _offline = [];
    /// <summary>Journal ends of in-progress jobs that weren't picked up, so a journal is read once, not every tick.</summary>
    private readonly Dictionary<Guid, JournalEnd?> _journalEnds = [];
    /// <summary>The job picked by hand; not switched away from when the aircraft type doesn't match.</summary>
    private Guid? _chosen;
    private DateTimeOffset? _deboardedAt;
    private volatile string? _settleNow;
    public SettlementResult? LastSettlement { get; private set; }
    public MissionContract? LastSettledContract { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised when tracking is armed for a job (e.g. to point GSX at it).</summary>
    public event Action<Guid>? Armed;
    /// <summary>Raised when a job in flight is picked up again after an app restart.</summary>
    public event Action<Guid>? Resumed;

    /// <summary>Tells whether the destination's ground services (GSX) are still busy; null = never wait.</summary>
    public Func<Guid, GroundServiceState>? GroundServices { get; set; }

    /// <summary>GSX services (catering, cleaning, water, lavatory) completed before departure; stored with the job at liftoff.</summary>
    public Func<Guid, IReadOnlyList<GroundService>>? PreDepartureServices { get; set; }

    /// <summary>Why the parked job isn't settled yet (waiting for GSX deboarding); null otherwise.</summary>
    public string? Waiting { get; private set; }

    /// <summary>Settles the parked job on the next tick without waiting for ground services.</summary>
    public void SettleNow(Guid contractId) => _settleNow = contractId.ToString();
    public event Action? Changed;

    /// <summary>Call when career data changed (accept, cancel, OFP, fleet …) so the next tick reloads it.</summary>
    public void Invalidate() => _dirty = true;

    public void ClearSettlement()
    {
        LastSettlement = null;
        LastSettledContract = null;
        Changed?.Invoke();
    }

    /// <summary>Switches the armed job while still on the ground.</summary>
    public bool Choose(Guid contractId)
    {
        if (host.Phase is not (FlightPhase.Preflight or FlightPhase.TaxiOut) || host.ContractId == contractId) return false;
        if (_jobs.FirstOrDefault(j => j.Id == contractId) is not { Status: ContractStatus.Accepted }) return false;
        host.EndFlight();
        Arm(contractId);
        _chosen = contractId;
        return true;
    }

    public async Task TickAsync()
    {
        if (career.ActiveCareerId is null) return;
        try
        {
            if (_dirty) await ReloadAsync();
            var t = host.Connected ? host.Latest : null;
            var sim = t is null ? null : new SimAircraftState(t.AircraftTitle, t.Position, t.OnGround, t.PayloadKg, t.FuelKg, t.TotalWeightKg);

            var readiness = new Dictionary<Guid, DepartureReadiness>();
            foreach (var job in _jobs.Where(j => j.Status == ContractStatus.Accepted))
                if (Evaluate(job, sim, _gameTime) is { } r) readiness[job.Id] = r;
            Readiness = readiness;
            // The light at liftoff decides whether the job starts; keep the last one taken on the ground.
            if (host.ContractId is { } armed && t is { OnGround: true } && host.Phase is FlightPhase.Preflight or FlightPhase.TaxiOut or FlightPhase.TakeoffRoll
                && readiness.TryGetValue(armed, out var ground))
                _groundReadiness = ground;

            if (host.HasSession && host.ContractId is { } current) await FollowAsync(current, t);
            else if (!host.HasSession && t is not null && !TryResume(t) && t.OnGround)
            {
                TryArm(t);
                if (host.ContractId is { } armedNow && _jobs.FirstOrDefault(j => j.Id == armedNow) is { } armedJob) await RecordStartWearAsync(armedJob);
            }

            LastError = null;
        }
        catch (Exception ex) when (ex is CareerException or InvalidOperationException or IOException)
        {
            // Ticks run every second; log a lasting problem once.
            if (ex.Message != LastError) AppLog.Warn("Flight automation", ex.Message, ex is CareerException ? null : ex);
            LastError = ex.Message;
        }
        Changed?.Invoke();
    }

    private async Task FollowAsync(Guid contractId, Telemetry? t)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == contractId);
        if (job is null)
        {
            // Cancelled, expired or settled elsewhere.
            host.EndFlight();
            return;
        }

        var phase = host.Phase;
        if (job.Status == ContractStatus.Accepted)
        {
            if (phase is FlightPhase.Preflight or FlightPhase.TaxiOut or FlightPhase.TakeoffRoll)
            {
                if (phase == FlightPhase.Preflight && t is { OnGround: true }) await RecordStartWearAsync(job);
                if (t is not null && phase != FlightPhase.TakeoffRoll && _airports.TryGetValue(job.Origin, out var origin) &&
                    t.Position.DistanceNmTo(origin.Position) > DisarmDistanceNm)
                {
                    host.Log(L.F("Tracking for {0} → {1} ended: aircraft no longer at {0}", job.Origin, job.Destination));
                    host.EndFlight();
                }
                // Another aircraft loaded in the sim: follow the job of the aircraft that is there now.
                else if (t is { OnGround: true } && phase != FlightPhase.TakeoffRoll && _chosen != job.Id && !MatchesSim(job, t)
                         && Candidates(t).FirstOrDefault() is { } other && other.Id != job.Id && MatchesSim(other, t))
                {
                    host.Log(L.F("Aircraft changed in the sim: switching from {0} → {1} to {2} → {3}", job.Origin, job.Destination, other.Origin, other.Destination));
                    host.EndFlight();
                    Arm(other.Id);
                }
                return;
            }
            if (phase is FlightPhase.Crashed or FlightPhase.Arrived && host.Summarize()?.TakeoffTime is null)
            {
                host.EndFlight();
                return;
            }
            await TakeOffAsync(job);
            return;
        }

        if (job.Status == ContractStatus.InProgress && job.SimWearAtStart is null && !_offline.Contains(job.Id) && phase is not (FlightPhase.Arrived or FlightPhase.Crashed))
        {
            // Not read at the stand (e.g. resumed after an app restart): better late than never, the rest still counts.
            await RecordStartWearAsync(job);
            if (job.SimWearAtStart is not null) _wearFromResume.Add(job.Id);
        }
        if (job.Status != ContractStatus.InProgress || phase is not (FlightPhase.Arrived or FlightPhase.Crashed))
        {
            Waiting = null;
            return;
        }
        if (phase == FlightPhase.Arrived && !_offline.Contains(job.Id) && !_wearAtArrival.ContainsKey(job.Id) && job.SimWearAtStart is not null
            && await host.ReadWearAsync() is { } arrivalWear)
            _wearAtArrival[job.Id] = arrivalWear;
        if (phase == FlightPhase.Arrived && !_offline.Contains(job.Id) && _settleNow != job.Id.ToString() && WaitForGroundServices(job, t)) return;
        _settleNow = null;
        Waiting = null;
        _deboardedAt = null;
        await SettleAsync(job);
    }

    /// <summary>
    /// With GSX in use the settlement waits for deboarding (its receipts belong to the flight): until it completed (plus
    /// a short pause for the receipt), at most <see cref="DeboardingStartTimeout"/> if it never starts, and not once the
    /// aircraft moves or starts an engine again.
    /// </summary>
    private bool WaitForGroundServices(MissionContract job, Telemetry? t)
    {
        var state = GroundServices?.Invoke(job.Id) ?? GroundServiceState.NotUsed;
        if (state == GroundServiceState.NotUsed || t is null || t.AnyEngineRunning || !t.OnGround || t.GroundSpeedKt > 3)
            return false;
        var parked = host.Summarize()?.BlockOn ?? t.Timestamp;
        var since = t.Timestamp - parked;
        switch (state)
        {
            case GroundServiceState.Completed:
                _deboardedAt ??= t.Timestamp;
                if (t.Timestamp - _deboardedAt < ReceiptGrace)
                {
                    Waiting = L.F("GSX deboarding completed, waiting for the last GSX receipts (at most {0:N0} min)",
                        Math.Max(1, Math.Ceiling((ReceiptGrace - (t.Timestamp - _deboardedAt.Value)).TotalMinutes)));
                    return true;
                }
                return false;
            case GroundServiceState.InProgress:
                _deboardedAt = null;
                Waiting = L.T("Waiting for GSX deboarding to finish");
                return since < DeboardingTimeout;
            default:
                _deboardedAt = null;
                Waiting = L.F("Waiting for GSX deboarding (at most {0:N0} min)", Math.Max(1, (DeboardingStartTimeout - since).TotalMinutes));
                return since < DeboardingStartTimeout;
        }
    }

    private async Task TakeOffAsync(MissionContract job)
    {
        var check = _groundReadiness;
        var blocking = check?.Blocking.ToList() ?? [];
        if (check is null || check[DepartureCheck.Sim]?.Light != Light.Green)
            blocking.Add(new CheckItem(DepartureCheck.Sim, L.T("Simulator"), Light.Red, L.T("no data from the ground")));
        if (blocking.Count > 0)
        {
            string reason = string.Join("; ", blocking.Select(b => $"{b.Label}: {b.Detail}"));
            _notStarted[job.Id] = reason;
            host.Log(L.F("Job {0} → {1} not started: {2}", job.Origin, job.Destination, reason));
            host.EndFlight();
            return;
        }

        _notStarted.Remove(job.Id);
        host.DepartureIssues = check!.Penalised.Select(i => $"{i.Label} ({i.Detail})").ToList();
        var services = PreDepartureServices?.Invoke(job.Id) ?? [];
        await career.StartFlightAsync(job.Id, host.DepartureIssues, host.Summarize()?.FuelAtBlockOffKg, services);
        if (services.Count > 0)
            AppLog.Info("Flight", $"GSX services before departure: {string.Join(", ", services)}");
        host.Log(host.DepartureIssues.Count == 0
            ? L.F("Job {0} → {1} started", job.Origin, job.Destination)
            : L.F("Job started with {0} deviation(s): {1}", host.DepartureIssues.Count, string.Join("; ", host.DepartureIssues)));
        _dirty = true;
    }

    /// <summary>
    /// Stores the sim's wear once per job while it waits at the stand (nothing wears there, so this equals block-off);
    /// the settlement charges the difference to the reading after parking.
    /// </summary>
    private async Task RecordStartWearAsync(MissionContract job)
    {
        if (job.SimWearAtStart is not null) return;
        // The sim's wear belongs to the aircraft loaded there, which must be the job's type.
        if (!MatchesSim(job, host.Latest)) return;
        if (_wearTried.TryGetValue(job.Id, out var last) && DateTime.UtcNow - last < WearRetry) return;
        bool first = !_wearTried.ContainsKey(job.Id);
        _wearTried[job.Id] = DateTime.UtcNow;
        if (await host.ReadWearAsync() is not { } wear)
        {
            if (first) AppLog.Warn("Wear", $"MSFS wear for {job.Origin} → {job.Destination} not readable yet, retrying every {WearRetry.TotalSeconds:N0} s");
            return;
        }
        await career.RecordSimWearAsync(job.Id, wear);
        job.SimWearAtStart = wear.Serialize();
        AppLog.Info("Wear", $"Start wear recorded ({wear.Levels.Count} components, worst {wear.WorstLevel:P0})");
    }

    private async Task SettleAsync(MissionContract job)
    {
        var summary = host.Summarize() ?? throw new CareerException(L.T("No flight data available."));
        var track = await host.SaveTrackAsync($"{job.Origin}-{job.Destination}-{job.Id:N}");
        // Stored with the job at liftoff, so they survive an app restart mid-flight.
        var issues = job.DepartureIssueList.Count > 0 ? job.DepartureIssueList : host.DepartureIssues;
        bool offline = _offline.Remove(job.Id);
        var result = FlightResultMapper.Map(summary, issues, offline ? null : await WearDeltaAsync(job));
        if (offline)
            result = result with { Notes = [.. result.Notes, L.T("Settled after an app restart from the recorded flight data; MSFS wear not recorded.")] };
        else if (job.SimWearAtStart is null)
            result = result with { Notes = [.. result.Notes, L.T("MSFS wear not recorded (no start value).")] };
        else if (_wearFromResume.Remove(job.Id))
            result = result with { Notes = [.. result.Notes, L.T("MSFS wear only recorded from the app restart onwards.")] };
        LastSettlement = await career.CompleteFlightAsync(job.Id, result, track);
        LastSettledContract = job;
        host.Log(L.F("Flight {0} → {1} settled automatically: {2}", job.Origin, job.Destination, LastSettlement.Outcome));
        host.EndFlight();
        _dirty = true;
    }

    private async Task<AircraftWear?> WearDeltaAsync(MissionContract job)
    {
        var start = AircraftWear.Parse(job.SimWearAtStart);
        if (start.IsEmpty)
        {
            AppLog.Warn("Wear", $"No start wear stored for {job.Origin} → {job.Destination}; components stay unchanged");
            return null;
        }
        if ((_wearAtArrival.Remove(job.Id, out var parked) ? parked : await host.ReadWearAsync()) is not { } end)
        {
            AppLog.Warn("Wear", "MSFS wear not readable after the flight; components stay unchanged");
            return null;
        }
        var delta = AircraftWear.Delta(start, end);
        AppLog.Info("Wear", $"Wear {job.Origin} → {job.Destination}: avg {delta.AverageLoss:P3}, " +
                                 string.Join(", ", delta.GroupLosses().Take(3).Select(g => $"{g.Group} −{g.Loss:P2}")));
        return delta;
    }

    /// <summary>
    /// After an app restart: picks up a job that is still flying, but only with its aircraft type loaded, in the air or
    /// near the flight (last journal position, origin or destination). A flight whose journal already ended while
    /// another aircraft is loaded is settled from the journal alone; anything else waits and doesn't block arming.
    /// </summary>
    private bool TryResume(Telemetry t)
    {
        foreach (var job in _jobs.Where(j => j.Status == ContractStatus.InProgress && _fleet.ContainsKey(j.AircraftId))
                     .OrderByDescending(j => MatchesSim(j, t)))
        {
            if (!_journalEnds.TryGetValue(job.Id, out var end)) _journalEnds[job.Id] = end = host.ReadJournalEnd(job.Id);
            var startedAt = job.FlightStartedAtUtc is { } utc ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) : t.Timestamp;
            if (MatchesSim(job, t) && NearFlight(job, t, end))
            {
                if (!host.ResumeFlight(job.Id, startedAt, job.FuelAtBlockOffKg)) continue;
                _journalEnds.Remove(job.Id);
                host.Log(L.F("Flight {0} → {1} resumed after restart", job.Origin, job.Destination));
                Resumed?.Invoke(job.Id);
                return true;
            }
            if (end is { Phase: FlightPhase.Arrived or FlightPhase.Crashed } && host.ResumeFlight(job.Id, startedAt, job.FuelAtBlockOffKg, live: false))
            {
                _journalEnds.Remove(job.Id);
                _offline.Add(job.Id);
                host.Log(L.F("Flight {0} → {1} is settled from its recorded data: another aircraft is loaded in the sim", job.Origin, job.Destination));
                return true;
            }
        }
        return false;
    }

    private bool NearFlight(MissionContract job, Telemetry t, JournalEnd? end)
    {
        if (!t.OnGround) return true;
        var places = new List<SkyCareer.Domain.GeoPosition>();
        if (end is not null) places.Add(end.Last.Position);
        foreach (var icao in new[] { job.Origin, job.Destination })
            if (_airports.TryGetValue(icao, out var airport)) places.Add(airport.Position);
        return places.Any(p => t.Position.DistanceNmTo(p) <= DepartureCheck.MaxDistanceNm);
    }

    private bool MatchesSim(MissionContract job, Telemetry? t) =>
        t is not null && _fleet.TryGetValue(job.AircraftId, out var aircraft) && career.Profile(aircraft.ProfileId).MatchesSimTitle(t.AircraftTitle);

    /// <summary>
    /// Accepted jobs whose aircraft and the sim are at the departure airport, the loaded type first. A job of another
    /// type is still armed when nothing matches, so the departure light shows what is wrong; fuel, GSX and wear are then
    /// left alone.
    /// </summary>
    private List<MissionContract> Candidates(Telemetry t) => _jobs
        .Where(j => j.Status == ContractStatus.Accepted && _fleet.TryGetValue(j.AircraftId, out var a) && a.Location == j.Origin
                    && _airports.TryGetValue(j.Origin, out var o) && t.Position.DistanceNmTo(o.Position) <= DepartureCheck.MaxDistanceNm)
        .OrderByDescending(j => MatchesSim(j, t))
        .ThenBy(j => j.Deadline)
        .ToList();

    private void TryArm(Telemetry t)
    {
        if (Candidates(t).FirstOrDefault() is { } job) Arm(job.Id);
    }

    private void Arm(Guid contractId)
    {
        _chosen = null;
        _groundReadiness = Readiness.GetValueOrDefault(contractId);
        host.BeginFlight(contractId);
        host.DepartureIssues = [];
        _notStarted.Remove(contractId);
        var job = _jobs.First(j => j.Id == contractId);
        host.Log(L.F("Ready for {0} → {1}: changes are possible until liftoff", job.Origin, job.Destination));
        Armed?.Invoke(contractId);
    }

    private DepartureReadiness? Evaluate(MissionContract job, SimAircraftState? sim, DateTime gameTime)
    {
        if (!_fleet.TryGetValue(job.AircraftId, out var aircraft) || !_airports.TryGetValue(job.Origin, out var origin)) return null;
        var profile = career.Profile(aircraft.ProfileId);
        return DepartureCheck.Evaluate(job, career.MissionType(job.MissionTypeId), profile, aircraft,
            career.BuildLoadSheet(job, profile), origin, gameTime, sim);
    }

    private async Task ReloadAsync()
    {
        _dirty = false;
        _gameTime = (await career.GetCareerAsync()).GameTime;
        _jobs = await career.GetContractsAsync(ContractStatus.Accepted, ContractStatus.InProgress);
        _fleet = (await career.GetFleetAsync()).ToDictionary(f => f.Id);
        _airports = await career.GetAirportsAsync(_jobs.SelectMany(j => new[] { j.Origin, j.Destination }).Distinct());
    }
}
