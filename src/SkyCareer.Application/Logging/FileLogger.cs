using System.Text;
using Microsoft.Extensions.Logging;

namespace SkyCareer.Application.Logging;

/// <summary>
/// Writes one log file per day (<c>skycareer-yyyyMMdd.log</c>) so testers can send what happened. Thread-safe;
/// files older than <see cref="KeepDays"/> are removed on start. Also an <see cref="ILoggerProvider"/>, so framework
/// logs (e.g. Blazor component errors) end up in the same file.
/// </summary>
public sealed class FileLogger : ILoggerProvider
{
    public const int KeepDays = 14;
    private const string Prefix = "skycareer-";

    private readonly object _lock = new();
    private readonly Func<DateTime> _now;

    public FileLogger(string directory, Func<DateTime>? now = null)
    {
        Directory = directory;
        _now = now ?? (() => DateTime.Now);
        System.IO.Directory.CreateDirectory(directory);
        CleanUp();
    }

    public string Directory { get; }
    public string CurrentFile => Path.Combine(Directory, $"{Prefix}{_now():yyyyMMdd}.log");

    public void Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        var line = new StringBuilder()
            .Append(_now().ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
            .Append(Short(level)).Append(' ')
            .Append(category).Append(": ").Append(message);
        if (exception is not null) line.AppendLine().Append(exception);
        lock (_lock)
        {
            try
            {
                File.AppendAllText(CurrentFile, line.AppendLine().ToString());
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    /// <summary>The last <paramref name="count"/> lines of today's (or the newest) log file.</summary>
    public IReadOnlyList<string> Tail(int count)
    {
        lock (_lock)
        {
            var file = new DirectoryInfo(Directory).GetFiles($"{Prefix}*.log").OrderByDescending(f => f.Name).FirstOrDefault();
            if (file is null) return [];
            var lines = File.ReadAllLines(file.FullName);
            return lines.Skip(Math.Max(0, lines.Length - count)).ToList();
        }
    }

    private void CleanUp()
    {
        var cutoff = _now().Date.AddDays(-KeepDays);
        foreach (var f in new DirectoryInfo(Directory).GetFiles($"{Prefix}*.log"))
        {
            if (DateTime.TryParseExact(f.Name[Prefix.Length..^4], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day) && day < cutoff)
            {
                try { f.Delete(); }
                catch (IOException) { }
            }
        }
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF",
        LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "   "
    };

    public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Category(FileLogger owner, string name) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(logLevel, name, formatter(state, exception), exception);
        }
    }
}

/// <summary>Static access to the app's log for code without dependency injection. No-op until <see cref="Initialize"/>.</summary>
public static class AppLog
{
    public static FileLogger? Logger { get; private set; }

    public static void Initialize(FileLogger logger) => Logger = logger;

    public static void Info(string category, string message) => Logger?.Write(LogLevel.Information, category, message);
    public static void Warn(string category, string message, Exception? ex = null) => Logger?.Write(LogLevel.Warning, category, message, ex);
    public static void Error(string category, string message, Exception? ex = null) => Logger?.Write(LogLevel.Error, category, message, ex);
}
