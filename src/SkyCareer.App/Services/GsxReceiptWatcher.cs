using System.Diagnostics;
using SkyCareer.Application;
using SkyCareer.Application.Logging;
using SkyCareer.Data;
using SkyCareer.Integrations.Gsx;

namespace SkyCareer.App.Services;

/// <summary>
/// While the career's GSX cost option is on, looks for new GSX receipts every ten seconds (and right away at start, for
/// receipts written while the app was closed) and books them via <see cref="CareerService.BookGsxReceiptsAsync"/>.
/// Works without a sim connection; receipts that fit no job or aircraft yet are retried on every scan.
/// </summary>
public sealed class GsxReceiptWatcher : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly CareerService _career;
    private readonly FlightMonitorService _monitor;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _reportedUnmatched = [];
    private string? _lastError;

    public GsxReceiptWatcher(CareerService career, FlightMonitorService monitor)
    {
        _career = career;
        _monitor = monitor;
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <summary>Copies of the HTML invoices, next to the database.</summary>
    public static string StoreDirectory => Path.Combine(Path.GetDirectoryName(SkyCareerDb.DefaultPath)!, "receipts");

    /// <summary>Opens a stored GSX invoice in the default browser (print or save as PDF from there).</summary>
    public static bool Open(string? htmlPath)
    {
        if (htmlPath is null || !File.Exists(htmlPath)) return false;
        Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        return true;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ScanAsync().ConfigureAwait(false);
                _lastError = null;
            }
            catch (Exception ex) when (ex is CareerException or InvalidOperationException or IOException)
            {
                if (ex.Message != _lastError) AppLog.Warn("GSX-Belege", ex.Message, ex is CareerException ? null : ex);
                _lastError = ex.Message;
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task ScanAsync()
    {
        if (_career.ActiveCareerId is null) return;
        var career = await _career.GetCareerAsync().ConfigureAwait(false);
        if (!career.GsxCostsEnabled || career.GsxCostsSinceUtc is not { } since) return;

        var receipts = GsxReceiptReader.Scan(GsxReceiptReader.DefaultRoot, since);
        if (receipts.Count == 0) return;
        var booked = await _career.BookGsxReceiptsAsync(receipts, _monitor.ContractId, StoreDirectory).ConfigureAwait(false);
        foreach (var r in booked)
            _monitor.Log($"GSX-Beleg gebucht: {r.Title} {r.Icao} {r.AmountEur:N2} €");

        var bookedIds = booked.Select(b => b.ReceiptId).ToHashSet();
        var known = (await _career.GetGsxReceiptsAsync().ConfigureAwait(false)).Select(r => r.ReceiptId).ToHashSet();
        foreach (var r in receipts.Where(r => !bookedIds.Contains(r.ReceiptId) && !known.Contains(r.ReceiptId)))
            if (_reportedUnmatched.Add(r.ReceiptId))
                AppLog.Info("GSX-Belege", $"{r.ReceiptId} ({r.Icao}) passt zu keinem Auftrag oder Flugzeug – wird später erneut versucht");
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        return ValueTask.CompletedTask;
    }
}
