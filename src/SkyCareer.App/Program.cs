using Velopack;

namespace SkyCareer.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: handles Velopack's install/update/uninstall hooks and exits
        // right away when the installer or updater started the exe for one of them.
        VelopackApp.Build().Run();

        new App().Run();
    }
}
