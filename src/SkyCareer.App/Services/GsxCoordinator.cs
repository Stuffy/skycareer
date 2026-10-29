using SkyCareer.Application.Logging;
using SkyCareer.Domain.Loading;
using SkyCareer.Integrations.Gsx;
using SkyCareer.Sim;

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

    public Guid? ContractId { get; private set; }
    public bool SyncEnabled { get; private set; }
    public GsxState? State { get; private set; }
    public string? Mismatch { get; private set; }
    public string? LastAction { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    /// <summary>Starts (or retargets) polling for a job. Calling again for the same job only updates the sheet/setting.</summary>
    public void Track(Guid contractId, LoadSheet sheet, bool syncEnabled)
    {
        lock (_lock)
        {
            _sheet = sheet;
            SyncEnabled = syncEnabled;
            if (ContractId == contractId && _cts is not null) return;

            _cts?.Cancel();
            ContractId = contractId;
            _sync = new GsxLoadSync();
            State = null;
            Mismatch = LastAction = LastError = null;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    public void SetSyncEnabled(bool enabled)
    {
        SyncEnabled = enabled;
        if (enabled) _sync = new GsxLoadSync(); // re-evaluate from the current GSX state
        Changed?.Invoke();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
            ContractId = null;
            State = null;
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
        var sheet = _sheet;
        if (sheet is null) return;
        var crew = sheet.Aircraft.CrewIncludedInEmptyWeight ? 0 : sheet.Aircraft.Pilots + sheet.Aircraft.CabinCrewCount;
        Mismatch = GsxLoadSync.PassengerMismatch(state, sheet.Passengers, crew);

        if (!SyncEnabled || monitor.Loader is not { } loader || sheet.Aircraft.Loading != Domain.Aircraft.LoadingMethod.SimConnectStations) return;
        // Only on the ground at the gate: before block-off (no session yet or preflight) and after parking.
        // Writing payload in between would be flagged as a payload change by the integrity monitor.
        if (monitor.Phase is not (null or Sim.Tracking.FlightPhase.Preflight or Sim.Tracking.FlightPhase.Arrived)) return;
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
