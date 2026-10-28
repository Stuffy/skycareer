using SkyCareer.Application.Logging;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Departure;
using SkyCareer.Missions.Settlement;
using SkyCareer.Sim;
using SkyCareer.Sim.Tracking;

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
    /// <summary>Continues tracking a job that is already flying (journal replay, or picked up from the current state).</summary>
    bool ResumeFlight(Guid contractId, DateTimeOffset startedAt);
    void EndFlight();
    FlightSummary? Summarize();
    Task<string?> SaveTrackAsync(string name);
    void Log(string message);
}

/// <summary>
/// Runs a career flight without a start button: arms tracking when the aircraft sits at the departure airport of an
/// accepted job, keeps the job editable on the ground, starts it at liftoff (unless position or type are wrong) and
/// settles it once the aircraft is parked again. Also evaluates the departure traffic light for every accepted job.
/// Not thread-safe by itself: the caller serialises <see cref="TickAsync"/>.
/// </summary>
public sealed class FlightAutomation(CareerService career, IFlightSessionHost host)
{
    /// <summary>An armed job is dropped when the aircraft shows up this far from its departure airport before takeoff.</summary>
    public const double DisarmDistanceNm = 5;

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
    public SettlementResult? LastSettlement { get; private set; }
    public MissionContract? LastSettledContract { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised when tracking is armed for a job (e.g. to point GSX at it).</summary>
    public event Action<Guid>? Armed;
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
            else if (!host.HasSession && t is not null && !TryResume(t) && t.OnGround) TryArm(t);

            LastError = null;
        }
        catch (Exception ex) when (ex is CareerException or InvalidOperationException or IOException)
        {
            // Ticks run every second; log a lasting problem once.
            if (ex.Message != LastError) AppLog.Warn("Flugautomatik", ex.Message, ex is CareerException ? null : ex);
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
                if (t is not null && phase != FlightPhase.TakeoffRoll && _airports.TryGetValue(job.Origin, out var origin) &&
                    t.Position.DistanceNmTo(origin.Position) > DisarmDistanceNm)
                {
                    host.Log($"Tracking für {job.Origin} → {job.Destination} beendet: Flugzeug nicht mehr in {job.Origin}");
                    host.EndFlight();
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

        if (job.Status == ContractStatus.InProgress && phase is FlightPhase.Arrived or FlightPhase.Crashed) await SettleAsync(job);
    }

    private async Task TakeOffAsync(MissionContract job)
    {
        var check = _groundReadiness;
        var blocking = check?.Blocking.ToList() ?? [];
        if (check is null || check[DepartureCheck.Sim]?.Light != Light.Green)
            blocking.Add(new CheckItem(DepartureCheck.Sim, "Simulator", Light.Red, "keine Daten vom Boden"));
        if (blocking.Count > 0)
        {
            string reason = string.Join("; ", blocking.Select(b => $"{b.Label}: {b.Detail}"));
            _notStarted[job.Id] = reason;
            host.Log($"Auftrag {job.Origin} → {job.Destination} nicht gestartet: {reason}");
            host.EndFlight();
            return;
        }

        _notStarted.Remove(job.Id);
        host.DepartureIssues = check!.Penalised.Select(i => $"{i.Label} ({i.Detail})").ToList();
        await career.StartFlightAsync(job.Id, host.DepartureIssues);
        host.Log(host.DepartureIssues.Count == 0
            ? $"Auftrag {job.Origin} → {job.Destination} gestartet"
            : $"Auftrag gestartet mit {host.DepartureIssues.Count} Abweichung(en): {string.Join("; ", host.DepartureIssues)}");
        _dirty = true;
    }

    private async Task SettleAsync(MissionContract job)
    {
        var summary = host.Summarize() ?? throw new CareerException("Keine Flugdaten vorhanden.");
        var track = await host.SaveTrackAsync($"{job.Origin}-{job.Destination}-{job.Id:N}");
        // Stored with the job at liftoff, so they survive an app restart mid-flight.
        var issues = job.DepartureIssueList.Count > 0 ? job.DepartureIssueList : host.DepartureIssues;
        LastSettlement = await career.CompleteFlightAsync(job.Id, FlightResultMapper.Map(summary, issues), track);
        LastSettledContract = job;
        host.Log($"Flug {job.Origin} → {job.Destination} automatisch abgerechnet: {LastSettlement.Outcome}");
        host.EndFlight();
        _dirty = true;
    }

    /// <summary>After an app restart: picks up a job that is still flying (preferring the one whose aircraft is loaded).</summary>
    private bool TryResume(Telemetry t)
    {
        var job = _jobs
            .Where(j => j.Status == ContractStatus.InProgress && _fleet.ContainsKey(j.AircraftId))
            .OrderByDescending(j => career.Profile(_fleet[j.AircraftId].ProfileId).MatchesSimTitle(t.AircraftTitle))
            .FirstOrDefault();
        if (job is null) return false;
        var startedAt = job.FlightStartedAtUtc is { } utc ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) : t.Timestamp;
        if (!host.ResumeFlight(job.Id, startedAt)) return false;
        host.Log($"Flug {job.Origin} → {job.Destination} nach Neustart fortgesetzt");
        return true;
    }

    private void TryArm(Telemetry t)
    {
        var candidates = _jobs
            .Where(j => j.Status == ContractStatus.Accepted && _fleet.TryGetValue(j.AircraftId, out var a) && a.Location == j.Origin
                        && _airports.TryGetValue(j.Origin, out var o) && t.Position.DistanceNmTo(o.Position) <= DepartureCheck.MaxDistanceNm)
            .OrderByDescending(j => career.Profile(_fleet[j.AircraftId].ProfileId).MatchesSimTitle(t.AircraftTitle))
            .ThenBy(j => j.Deadline)
            .ToList();
        if (candidates.Count > 0) Arm(candidates[0].Id);
    }

    private void Arm(Guid contractId)
    {
        _groundReadiness = Readiness.GetValueOrDefault(contractId);
        host.BeginFlight(contractId);
        host.DepartureIssues = [];
        _notStarted.Remove(contractId);
        var job = _jobs.First(j => j.Id == contractId);
        host.Log($"Bereit für {job.Origin} → {job.Destination}: Änderungen sind bis zum Abheben möglich");
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
