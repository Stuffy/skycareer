using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Sim;

/// <summary>
/// Reads MSFS 2024's wear-and-tear components (<c>WEAR AND TEAR LEVEL:0..49</c>). They can be read but not set via
/// SimConnect (verified 2026-10-30), so SkyCareer only uses the change during a flight.
/// </summary>
public static class WearReader
{
    public static async Task<AircraftWear> ReadAsync(IAircraftInspector inspector, CancellationToken ct = default)
    {
        var raw = new Dictionary<int, double>();
        foreach (var chunk in WearComponents.All.Chunk(IAircraftInspector.MaxBatchVariables))
        {
            try
            {
                var values = await inspector.ReadVariablesAsync(chunk.Select(c => WearComponents.SimVar(c.Id)).ToList(), ct).ConfigureAwait(false);
                for (int i = 0; i < chunk.Length; i++) raw[chunk[i].Id] = values[i];
            }
            catch (InvalidOperationException)
            {
                // One rejected name fails the whole batch: read this block one by one and skip the bad ones.
                foreach (var c in chunk)
                {
                    try { raw[c.Id] = await inspector.ReadVariableAsync(WearComponents.SimVar(c.Id), ct: ct).ConfigureAwait(false); }
                    catch (InvalidOperationException) { }
                }
            }
        }
        return AircraftWear.FromSim(raw);
    }
}
