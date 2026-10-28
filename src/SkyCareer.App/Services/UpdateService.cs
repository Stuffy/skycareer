using SkyCareer.Application.Logging;
using System.Reflection;
using SkyCareer.Sim.Tracking;
using Velopack;
using Velopack.Sources;

namespace SkyCareer.App.Services;

public enum UpdateStatus { Disabled, Idle, Checking, Downloading, Ready, Error }

/// <summary>
/// Checks the public GitHub releases for a newer version (Velopack), downloads it in the background and applies it
/// on request or when the app closes. Never restarts during a flight. Disabled in development builds that weren't
/// installed by Velopack's setup.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly FlightMonitorService _monitor;
    private readonly UpdateManager? _manager;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private UpdateInfo? _pending;

    public UpdateService(FlightMonitorService monitor)
    {
        _monitor = monitor;
        RepoUrl = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoUrl")?.Value ?? "";
        CurrentVersion = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (manager.IsInstalled)
            {
                _manager = manager;
                CurrentVersion = manager.CurrentVersion?.ToString() ?? CurrentVersion;
                Status = UpdateStatus.Idle;
            }
        }
        catch (Exception ex)
        {
            // A broken install must not keep the app from starting.
            Error = ex.Message;
            AppLog.Warn("Updates", "Updater nicht verfügbar", ex);
        }
    }

    public string RepoUrl { get; }
    public string CurrentVersion { get; }
    public UpdateStatus Status { get; private set; } = UpdateStatus.Disabled;
    public string? AvailableVersion { get; private set; }
    public int Progress { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastCheckUtc { get; private set; }
    public string? ReleaseUrl => AvailableVersion is null || RepoUrl.Length == 0 ? null : $"{RepoUrl}/releases/tag/v{AvailableVersion}";

    /// <summary>False while a tracked flight is underway (anything between leaving the gate and parking again).</summary>
    public bool CanRestartNow => !_monitor.HasSession || _monitor.Phase is null or FlightPhase.Preflight or FlightPhase.Arrived;

    public event Action? Changed;

    /// <summary>Starts the periodic background check (first one shortly after startup).</summary>
    public void Start()
    {
        if (_manager is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FirstCheckDelay, _stop.Token).ConfigureAwait(false);
                while (!_stop.IsCancellationRequested)
                {
                    await CheckAndDownloadAsync().ConfigureAwait(false);
                    await Task.Delay(CheckInterval, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>Looks for a newer release and downloads it. Errors are kept in <see cref="Error"/>, never thrown.</summary>
    public async Task CheckAndDownloadAsync()
    {
        if (_manager is null || Status == UpdateStatus.Ready || !await _busy.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            Set(UpdateStatus.Checking);
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            LastCheckUtc = DateTime.UtcNow;
            if (info is null)
            {
                Set(UpdateStatus.Idle);
                return;
            }

            AvailableVersion = info.TargetFullRelease.Version.ToString();
            Progress = 0;
            Set(UpdateStatus.Downloading);
            await _manager.DownloadUpdatesAsync(info, p =>
            {
                Progress = p;
                Changed?.Invoke();
            }, _stop.Token).ConfigureAwait(false);
            _pending = info;
            AppLog.Info("Updates", $"Version {AvailableVersion} geladen");
            Set(UpdateStatus.Ready);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            AppLog.Warn("Updates", "Update-Prüfung fehlgeschlagen", ex);
            Set(UpdateStatus.Error);
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Installs the downloaded update and restarts the app; refused during a flight.</summary>
    public void RestartAndApply()
    {
        if (_manager is null || _pending is null) return;
        if (!CanRestartNow) throw new InvalidOperationException("Das Update wird nach dem Flug installiert.");
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    /// <summary>Called on exit: a downloaded update is installed silently once the app has closed.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _pending is null) return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception)
        {
            // Not worth blocking the exit; the next start downloads it again.
        }
    }

    private void Set(UpdateStatus status)
    {
        Status = status;
        if (status != UpdateStatus.Error) Error = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
