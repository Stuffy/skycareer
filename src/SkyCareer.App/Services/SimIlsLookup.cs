using SkyCareer.Application;

namespace SkyCareer.App.Services;

/// <summary>Answers ILS questions from the simulator's facility database (needs a live MSFS connection).</summary>
public sealed class SimIlsLookup(FlightMonitorService monitor) : IAirportIlsLookup
{
    public bool Available => monitor.Facilities is not null;

    public async Task<IReadOnlyDictionary<string, AirportIlsInfo?>> CheckAsync(IReadOnlyList<string> idents, CancellationToken ct = default)
    {
        var result = new Dictionary<string, AirportIlsInfo?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ident in idents)
        {
            if (monitor.Facilities is not { } reader) break; // connection lost mid-way: the rest stays unknown
            try
            {
                var info = await reader.ReadAirportAsync(ident, ct);
                result[ident] = info is null
                    ? null
                    : new AirportIlsInfo(info.HasIls, info.IlsApproachRunways.Count > 0
                        ? string.Join(" ", info.IlsApproachRunways)
                        : string.Join(" ", info.Runways.Where(r => r.HasIls).Select(r => r.Designator)));
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
            {
                // Slow or failed answer: leave the airport unknown so a later search can retry it.
            }
        }
        return result;
    }
}
