using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SkyCareer.App.Services;
using SkyCareer.Application;
using SkyCareer.Data;
using SkyCareer.Integrations.SimBrief;
using SkyCareer.Missions;
using SkyCareer.Missions.Generation;

namespace SkyCareer.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WebView2 paints white until the page loads; this makes it start in the app's dark background
        // (ARGB hex, read when the WebView2 environment is created, so it must be set before the window).
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF32333D");

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddMudServices();

        services.AddSingleton(new ContentStore(Path.Combine(AppContext.BaseDirectory, "content"), ContentStore.DefaultUserRoot));
        // Pages that only read content get the current snapshot when they are created.
        services.AddTransient(sp => sp.GetRequiredService<ContentStore>().Current);
        services.AddSingleton<IMissionGenerator, StandardMissionGenerator>();
        services.AddSingleton<MissionGeneratorRegistry>();
        services.AddSingleton<FlightMonitorService>();
        services.AddSingleton<GsxCoordinator>();
        services.AddSingleton<DepartureCoordinator>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<IAirportIlsLookup, SimIlsLookup>();
        services.AddSingleton<MarketFilter>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        services.AddSingleton<SimBriefClient>();

        var dbPath = SkyCareerDb.DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddDbContextFactory<SkyCareerDb>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<CareerService>();

        _services = services.BuildServiceProvider();
        using (var db = _services.GetRequiredService<IDbContextFactory<SkyCareerDb>>().CreateDbContext())
            db.Database.Migrate();
        var career = _services.GetRequiredService<CareerService>();
        _services.GetRequiredService<ContentStore>().Changed += career.NotifyContentChanged;
        // No career is preselected: the app opens on the career selection screen.
        // Created eagerly so flights are armed, started and settled whichever page is open.
        _services.GetRequiredService<DepartureCoordinator>();

        // Closing the main window ends the app even if another window (e.g. WebView dev tools) is still open.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow(_services);
        MainWindow.Show();

        // Connect to MSFS right away; the SimConnect source keeps retrying in the background until the sim runs.
        var monitor = _services.GetRequiredService<FlightMonitorService>();
        if (monitor.LiveAvailable) _ = monitor.ConnectLiveAsync();

        _services.GetRequiredService<UpdateService>().Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose on the thread pool: blocking the UI thread on async cleanup deadlocks as soon as any
        // continuation wants the UI context (that kept the process alive after closing the window while
        // connected to MSFS). The timeout guarantees the app exits even if the simulator doesn't respond.
        if (_services is { } services)
        {
            // A downloaded update is installed by Velopack's updater once this process has exited.
            services.GetRequiredService<UpdateService>().ApplyOnExit();
            Task.Run(() => services.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
        base.OnExit(e);
    }
}
