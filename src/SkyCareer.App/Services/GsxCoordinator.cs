using SkyCareer.Application;
using SkyCareer.Application.Logging;
using SkyCareer.Domain.Loading;
using SkyCareer.Integrations.Gsx;
using SkyCareer.Sim;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.App.Services;

/// <summary>
/// Polls GSX every two seconds for the job being prepared or flown and, when enabled, loads the aircraft in
/// step with GSX boarding/deboarding. Runs in the background independent of which page is open.
/// </summary>
public sealed class GsxCoordinator(FlightMonitorService monitor) : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private GsxLoadSync _sync = new();
    private LoadSheet? _sheet;
    private bool _departed;
    /// <summary>A job to switch to once the deboarding of the current (departed) one is over.</summary>
    private (Guid Id, LoadSheet Sheet, bool Sync, bool Costs, bool Departed)? _next;

    public Guid? ContractId { get; private set; }
    public bool SyncEnabled { get; private set; }
    /// <summary>GSX receipts are booked as costs; the settlement then waits for deboarding too.</summary>
    public bool CostsEnabled { get; private set; }
    public GsxState? State { get; private set; }
    public string? Mismatch { get; private set; }
    public string? LastAction { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Starts (or retargets) polling for a job. Calling again for the same job only updates the sheet/settings.
    /// <paramref name="departed"/>: the job already took off (e.g. after an app restart), so only deboarding is followed.
    /// While a departed job is still deboarding, a different job is queued until that is over.
    /// </summary>
    public void Track(Guid contractId, LoadSheet sheet, bool syncEnabled, bool costsEnabled = false, bool departed = false)
    {
        lock (_lock)
        {
            if (ContractId != contractId && _cts is not null && _departed && Deboarding(State))
            {
                _next = (contractId, sheet, syncEnabled, costsEnabled, departed);
                return;
            }
            _next = null;
            SyncEnabled = syncEnabled;
            CostsEnabled = costsEnabled;
            if (ContractId == contractId && _cts is not null)
            {
                _sheet = sheet;
                _departed |= departed;
                return;
            }

            _cts?.Cancel();
            _sheet = sheet;
            ContractId = contractId;
            _departed = departed;
            _sync = new GsxLoadSync(departed);
            State = null;
            Mismatch = LastAction = LastError = null;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    private static bool Deboarding(GsxState? s) => s is { Detected: true, Deboarding: GsxServiceState.Requested or GsxServiceState.Performing };

    /// <summary>Whether the settlement of this job should wait for GSX deboarding (see <see cref="FlightAutomation"/>).</summary>
    public GroundServiceState GroundServices(Guid contractId)
    {
        if (ContractId != contractId || !(SyncEnabled || CostsEnabled) || State is not { Detected: true } s) return GroundServiceState.NotUsed;
        return s.Deboarding switch
        {
            GsxServiceState.Completed => GroundServiceState.Completed,
            GsxServiceState.Requested or GsxServiceState.Performing => GroundServiceState.InProgress,
            _ => GroundServiceState.NotStarted
        };
    }

    public void SetSyncEnabled(bool enabled)
    {
        SyncEnabled = enabled;
        if (enabled) _sync = new GsxLoadSync(_departed); // re-evaluate from the current GSX state
        Changed?.Invoke();
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_departed && Deboarding(State)) return; // finish the deboarding first
            _cts?.Cancel();
            _cts = null;
            ContractId = null;
            State = null;
            _next = null;
        }
        Changed?.Invoke();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PollAsync(ct);
                LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                if (ex.Message != LastError) AppLog.Warn("GSX", ex.Message, ex);
                LastError = ex.Message;
            }
            Changed?.Invoke();
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        if (monitor.Inspector is not { } inspector)
        {
            State = null;
            return;
        }

        var state = await new GsxClient(inspector).ReadAsync(ct);
        State = state;
        if (_next is { } next && !Deboarding(state))
        {
            _next = null;
            Track(next.Id, next.Sheet, next.Sync, next.Costs, next.Departed);
            return;
        }
        bool thisJob = monitor.ContractId == ContractId;
        if (thisJob && monitor.Phase is >= FlightPhase.TakeoffRoll and not FlightPhase.Crashed) _departed = true;
        var sheet = _sheet;
        if (sheet is null) return;
        var crew = sheet.Aircraft.CrewIncludedInEmptyWeight ? 0 : sheet.Aircraft.Pilots + sheet.Aircraft.CabinCrewCount;
        Mismatch = GsxLoadSync.PassengerMismatch(state, sheet.Passengers, crew);

        if (!SyncEnabled || monitor.Loader is not { } loader || sheet.Aircraft.Loading != Domain.Aircraft.LoadingMethod.SimConnectStations) return;
        // Only on the ground at the gate: before block-off and after parking. Writing payload in between would be
        // flagged as a payload change; right after an app restart the phase is unknown until the flight is resumed.
        if (!GsxLoadSync.MayWrite(monitor.Phase, monitor.Latest?.OnGround == true, _departed, thisJob)) return;
        if (_sync.Next(state) is not { } action) return;

        if (action.WritesPayload)
            await loader.ApplyPayloadAsync(GsxLoadSync.ScaleStations(sheet, action.PassengerFraction, action.CargoFraction), ct);
        if (action.ApplyFuel)
            await loader.ApplyFuelAsync(sheet.Fuel.Block, ct);
        LastAction = $"{DateTime.Now:HH:mm:ss} {action.Reason}" +
                     (action.WritesPayload ? $" – Kabine {action.PassengerFraction:P0}, Fracht {action.CargoFraction:P0}" : "") +
                     (action.ApplyFuel ? $" – Treibstoff {sheet.Fuel.Block.Kg:N0} kg" : "");
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        return ValueTask.CompletedTask;
    }
}
