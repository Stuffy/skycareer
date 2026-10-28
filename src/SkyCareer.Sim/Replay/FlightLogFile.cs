using System.Text.Json;

namespace SkyCareer.Sim.Replay;

/// <summary>Flight logs are stored as JSON Lines: one <see cref="Telemetry"/> per line, so a crash never corrupts earlier samples.</summary>
public static class FlightLogFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task AppendAsync(StreamWriter writer, Telemetry t)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(t, Json));
    }

    public static async IAsyncEnumerable<Telemetry> ReadAsync(string path)
    {
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            yield return JsonSerializer.Deserialize<Telemetry>(line, Json)
                ?? throw new InvalidDataException($"Invalid telemetry line in {path}");
        }
    }
}
