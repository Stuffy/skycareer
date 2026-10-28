using System.Diagnostics;
using System.Text;
using SkyCareer.Application;
using SkyCareer.Application.Logging;
using SkyCareer.Domain.Career;

namespace SkyCareer.App.Services;

/// <summary>What a tester sends with a bug report: versions, sim state, career overview and the recent log.</summary>
public sealed class Diagnostics(FlightMonitorService monitor, UpdateService updates, CareerService career)
{
    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(App.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.LogDirectory}\"") { UseShellExecute = true });
    }

    public async Task<string> ReportAsync(int logLines = 200)
    {
        var sb = new StringBuilder()
            .AppendLine($"SkyCareer {updates.CurrentVersion} ({(updates.Status == UpdateStatus.Disabled ? "Entwicklungsversion" : "installiert")})")
            .AppendLine($"Windows: {Environment.OSVersion}, .NET {Environment.Version}")
            .AppendLine($"Simulator: {monitor.SourceName} – {monitor.Connection}, SimConnect im Build: {(monitor.LiveAvailable ? "ja" : "nein")}")
            .AppendLine($"Flug: {(monitor.HasSession ? $"Phase {monitor.Phase}, Auftrag {monitor.ContractId}" : "keiner")}, Flugzeug: {monitor.Latest?.AircraftTitle ?? "–"}");
        if (career.ActiveCareerId is not null)
        {
            try
            {
                var c = await career.GetCareerAsync();
                var fleet = await career.GetFleetAsync();
                var jobs = await career.GetContractsAsync(ContractStatus.Accepted, ContractStatus.InProgress);
                sb.AppendLine($"Karriere: Spielzeit {c.GameTime:yyyy-MM-dd HH:mm}, {fleet.Count} Flugzeug(e) ({string.Join(", ", fleet.Select(f => f.ProfileId))}), " +
                              $"{jobs.Count} aktive Aufträge, Reputation {c.Reputation}");
            }
            catch (CareerException ex)
            {
                sb.AppendLine($"Karriere: {ex.Message}");
            }
        }
        sb.AppendLine().AppendLine($"--- Letzte {logLines} Log-Zeilen ---");
        foreach (var line in AppLog.Logger?.Tail(logLines) ?? []) sb.AppendLine(line);
        return sb.ToString();
    }

    /// <summary>Copies the report to the Windows clipboard (via WPF, the WebView's clipboard API needs a user gesture).</summary>
    public async Task CopyReportAsync()
    {
        string text = await ReportAsync();
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(text));
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            throw new InvalidOperationException("Die Zwischenablage ist gerade belegt – bitte noch einmal versuchen.", ex);
        }
    }
}
