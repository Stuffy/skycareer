using SkyCareer.Application;
using SkyCareer.Application.Logging;

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
        Automation = new FlightAutomation(career, monitor);
        Automation.Armed += OnArmed;
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
            var job = (await _career.GetContractsAsync(Domain.Career.ContractStatus.Accepted).ConfigureAwait(false)).FirstOrDefault(c => c.Id == contractId);
            var aircraft = job is null ? null : (await _career.GetFleetAsync().ConfigureAwait(false)).FirstOrDefault(f => f.Id == job.AircraftId);
            if (job is null || aircraft is null) return;
            var career = await _career.GetCareerAsync().ConfigureAwait(false);
            _gsx.Track(job.Id, _career.BuildLoadSheet(job, _career.Profile(aircraft.ProfileId)), career.GsxSyncEnabled);
            if (career.PersistFuel) await RestoreFuelAsync(job, aircraft).ConfigureAwait(false);
        }
        catch (CareerException)
        {
            // GSX is optional; the market page retargets it on its next load.
        }
    });

    /// <summary>
    /// Puts the fuel the aircraft had after its last flight back into the sim, once per job and before block-off; later
    /// uplift (EFB, GSX) is then paid at settlement.
    /// </summary>
    private async Task RestoreFuelAsync(Domain.Career.MissionContract job, Domain.Career.FleetAircraft aircraft)
    {
        if (job.FuelRestoredAtUtc is not null || aircraft.FuelKg is not { } stored || _monitor.Loader is not { } loader) return;
        if (_monitor.Latest is not { OnGround: true } t || _monitor.Phase is not (null or Sim.Tracking.FlightPhase.Preflight)) return;
        try
        {
            if (Math.Abs(t.FuelKg - stored) > Math.Max(20, stored * 0.02))
            {
                await loader.ApplyFuelAsync(Domain.Mass.FromKg(stored)).ConfigureAwait(false);
                _monitor.Log($"Tankstand von {aircraft.Registration} wiederhergestellt: {stored:N0} kg");
            }
            await _career.MarkFuelRestoredAsync(job.Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            AppLog.Warn("Treibstoff", $"Tankstand konnte nicht gesetzt werden: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _monitor.Changed -= OnMonitorChanged;
        _career.Changed -= OnCareerChanged;
    }
}
