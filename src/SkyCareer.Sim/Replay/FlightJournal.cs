namespace SkyCareer.Sim.Replay;

/// <summary>
/// Running record of a career flight (<c>active-{contractId}.jsonl</c>), so tracking can resume with the full history
/// after the app was closed or crashed mid-flight. Flushed at most once per second; deleted when the flight ends.
/// </summary>
public sealed class FlightJournal : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private readonly StreamWriter _writer;
    private DateTime _lastFlush = DateTime.MinValue;

    public FlightJournal(string directory, Guid contractId)
    {
        Directory.CreateDirectory(directory);
        Path = PathFor(directory, contractId);
        _writer = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    public string Path { get; }

    public static string PathFor(string directory, Guid contractId) => System.IO.Path.Combine(directory, $"active-{contractId:N}.jsonl");

    /// <summary>Samples recorded for the contract so far; empty if there is no journal.</summary>
    public static List<Telemetry> Read(string directory, Guid contractId)
    {
        var path = PathFor(directory, contractId);
        return File.Exists(path) ? FlightLogFile.ReadAll(path) : [];
    }

    public void Append(Telemetry t)
    {
        FlightLogFile.Append(_writer, t);
        if (DateTime.UtcNow - _lastFlush < FlushInterval) return;
        _writer.Flush();
        _lastFlush = DateTime.UtcNow;
    }

    /// <summary>Closes and removes the journal (flight settled or abandoned).</summary>
    public void Delete()
    {
        Dispose();
        try { File.Delete(Path); }
        catch (IOException) { }
    }

    public void Dispose() => _writer.Dispose();
}
