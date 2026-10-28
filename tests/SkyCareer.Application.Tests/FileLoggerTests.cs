using Microsoft.Extensions.Logging;
using SkyCareer.Application.Logging;

namespace SkyCareer.Application.Tests;

public class FileLoggerTests
{
    [Fact]
    public void Writes_daily_files_returns_the_tail_and_removes_old_logs()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"skycareer-logs-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "skycareer-20200101.log"), "old");
            var now = new DateTime(2026, 10, 28, 12, 0, 0);

            var log = new FileLogger(dir, () => now);
            Assert.False(File.Exists(Path.Combine(dir, "skycareer-20200101.log")));

            log.Write(LogLevel.Information, "App", "Start");
            log.CreateLogger("Blazor").LogError(new InvalidOperationException("kaputt"), "Komponente fehlgeschlagen");
            log.CreateLogger("Noise").LogDebug("ignored");

            Assert.EndsWith("skycareer-20261028.log", log.CurrentFile);
            var text = File.ReadAllText(log.CurrentFile);
            Assert.Contains("INF App: Start", text);
            Assert.Contains("ERR Blazor: Komponente fehlgeschlagen", text);
            Assert.Contains("kaputt", text);
            Assert.DoesNotContain("ignored", text);
            Assert.Contains("Start", log.Tail(200)[0]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
