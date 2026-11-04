using SkyCareer.Application;
using SkyCareer.Application.Logging;
using SkyCareer.Localization;

namespace SkyCareer.App.Services;

/// <summary>
/// Drives <see cref="FlightAutomation"/> in the background: at most once per second on telemetry and right after
/// career changes, never two ticks at a time. Points GSX at the job that gets armed. Independent of the open page.
/// </summary>
public sealed class DepartureCoordinator : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly FlightMonitorService _monitor;
    private readonly CareerService _career;
    private readonly GsxCoordinator _gsx;
    private readonly SemaphoreSlim _running = new(1, 1);
    private DateTime _lastTick;

    public DepartureCoordinator(FlightMonitorService monitor, CareerService career, GsxCoordinator gsx)
    {
        _monitor = monitor;
        _career = career;
        _gsx = gsx;
        Automation = new FlightAutomation(career, monitor) { GroundServices = gsx.GroundServices };
        Automation.Armed += OnArmed;
        Automation.Resumed += OnResumed;
        Automation.Changed += () => Changed?.Invoke();
        _monitor.Changed += OnMonitorChanged;
        _career.Changed += OnCareerChanged;
    }

    public FlightAutomation Automation { get; }
    public event Action? Changed;

    private void OnMonitorChanged()
    {
        if (DateTime.UtcNow - _lastTick < Interval) return;
        Kick();
    }

    private void OnCareerChanged()
    {
        Automation.Invalidate();
        Kick();
    }

    /// <summary>Evaluates right away instead of on the next telemetry sample (e.g. after a debug phase change).</summary>
    public void TickNow() => Kick();

    private void Kick()
    {
        if (!_running.Wait(0)) return;
        _lastTick = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try { await Automation.TickAsync().ConfigureAwait(false); }
            finally { _running.Release(); }
        });
    }

    private void OnArmed(Guid contractId) => _ = Task.Run(async () =>
    {
        try
        {
            if (await TrackGsxAsync(contractId, Domain.Career.ContractStatus.Accepted).ConfigureAwait(false) is not { } tracked) return;
            var (job, aircraft, career) = tracked;
            await _career.MarkArmedAsync(job.Id).ConfigureAwait(false);
            if (career.PersistFuel) await RestoreFuelAsync(job, aircraft).ConfigureAwait(false);
        }
        catch (CareerException)
        {
            // GSX is optional; the market page retargets it on its next load.
        }
    });

    /// <summary>After an app restart GSX follows the resumed job again, so its deboarding is synced (and waited for).</summary>
    private void OnResumed(Guid contractId) => _ = Task.Run(async () =>
    {
        try { await TrackGsxAsync(contractId, Domain.Career.ContractStatus.InProgress).ConfigureAwait(false); }
        catch (CareerException) { }
    });

    private async Task<(Domain.Career.MissionContract, Domain.Career.FleetAircraft, Domain.Career.Career)?> TrackGsxAsync(
        Guid contractId, Domain.Career.ContractStatus status)
    {
        var job = (await _career.GetContractsAsync(status).ConfigureAwait(false)).FirstOrDefault(c => c.Id == contractId);
        var aircraft = job is null ? null : (await _career.GetFleetAsync().ConfigureAwait(false)).FirstOrDefault(f => f.Id == job.AircraftId);
        if (job is null || aircraft is null) return null;
        // A job armed although another type is loaded (so its light shows why) gets no GSX, fuel or arming time.
        if (status == Domain.Career.ContractStatus.Accepted && !_career.Profile(aircraft.ProfileId).MatchesSimTitle(_monitor.Latest?.AircraftTitle))
            return null;
        var career = await _career.GetCareerAsync().ConfigureAwait(false);
        _gsx.Track(job.Id, _career.BuildLoadSheet(job, _career.Profile(aircraft.ProfileId)), career.GsxSyncEnabled, career.GsxCostsEnabled,
            departed: status == Domain.Career.ContractStatus.InProgress);
        return (job, aircraft, career);
    }

    /// <summary>How long after restoring the fuel a reset by the sim or the aircraft is undone.</summary>
    private static readonly TimeSpan RestoreWatch = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Puts the fuel the aircraft had after its last flight back into the sim, once per job and before block-off; later
    /// uplift (EFB, GSX) is then paid at settlement. MSFS (or the aircraft) may reset fuel and payload to its defaults
    /// right after loading into the flight (Citation X: 50 % fuel and empty stations two seconds after the restore,
    /// 2026-11-03), so the restore is watched for a minute and repeated when such a reset shows up.
    /// </summary>
    private async Task RestoreFuelAsync(Domain.Career.MissionContract job, Domain.Career.FleetAircraft aircraft)
    {
        if (job.FuelRestoredAtUtc is not null || aircraft.FuelKg is not { } stored || _monitor.Loader is not { } loader)
        {
            AppLog.Info("Fuel", job.FuelRestoredAtUtc is not null ? $"Fuel for {job.Origin} → {job.Destination} was already restored"
                : aircraft.FuelKg is null ? $"No stored fuel for {aircraft.Registration}" : "Not connected to the sim, fuel not restored");
            return;
        }
        // Still the armed job, and still its aircraft type in the sim.
        if (!StillPreparing(job, aircraft) || _monitor.Latest is not { OnGround: true } t) return;
        double tolerance = Math.Max(20, stored * 0.02);
        try
        {
            if (Math.Abs(t.FuelKg - stored) > tolerance)
            {
                await loader.ApplyFuelAsync(Domain.Mass.FromKg(stored)).ConfigureAwait(false);
                _monitor.Log(L.F("Fuel on board of {0} restored: {1:N0} kg", aircraft.Registration, stored));
            }
            else AppLog.Info("Fuel", $"Fuel on board of {aircraft.Registration} already at {t.FuelKg:N0} kg (stored {stored:N0} kg)");
            await _career.MarkFuelRestoredAsync(job.Id).ConfigureAwait(false);

            var watchUntil = DateTime.UtcNow + RestoreWatch;
            var before = _monitor.Latest;
            int repeats = 0;
            while (DateTime.UtcNow < watchUntil && repeats < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                if (!StillPreparing(job, aircraft) || _monitor.Latest is not { OnGround: true } now || before is null) return;
                // GSX refuelling (or boarding) takes over from here.
                if (_gsx.State is { Refueling: Integrations.Gsx.GsxServiceState.Requested or Integrations.Gsx.GsxServiceState.Performing }) return;
                // A reset changes fuel and payload at once; an EFB uplift alone is the player's and stays.
                bool reset = Math.Abs(now.FuelKg - before.FuelKg) > tolerance && Math.Abs(now.PayloadKg - before.PayloadKg) > 10
                             && Math.Abs(now.FuelKg - stored) > tolerance;
                if (reset)
                {
                    await loader.ApplyFuelAsync(Domain.Mass.FromKg(stored)).ConfigureAwait(false);
                    _monitor.Log(L.F("Fuel was reset by the sim to {0:N0} kg; restored {1:N0} kg again", now.FuelKg, stored));
                    repeats++;
                }
                before = _monitor.Latest;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            AppLog.Warn("Fuel", $"Could not set the fuel quantity: {ex.Message}", ex);
        }
    }

    private bool StillPreparing(Domain.Career.MissionContract job, Domain.Career.FleetAircraft aircraft) =>
        _monitor.ContractId == job.Id && _monitor.Phase is null or Sim.Tracking.FlightPhase.Preflight
        && _career.Profile(aircraft.ProfileId).MatchesSimTitle(_monitor.Latest?.AircraftTitle);

    public void Dispose()
    {
        _monitor.Changed -= OnMonitorChanged;
        _career.Changed -= OnCareerChanged;
    }
}
