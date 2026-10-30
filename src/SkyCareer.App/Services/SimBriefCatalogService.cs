using SkyCareer.Data;
using SkyCareer.Integrations.SimBrief;

namespace SkyCareer.App.Services;

/// <summary>SimBrief's aircraft type list, loaded on first use and cached next to the database for a week.</summary>
public sealed class SimBriefCatalogService(HttpClient http)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimBriefAircraftCatalog? _catalog;

    public static string CacheFile => Path.Combine(Path.GetDirectoryName(SkyCareerDb.DefaultPath)!, "simbrief-types.json");

    public async Task<SimBriefAircraftCatalog> GetAsync(CancellationToken ct = default)
    {
        if (_catalog is not null) return _catalog;
        await _gate.WaitAsync(ct);
        try
        {
            return _catalog ??= await SimBriefAircraftCatalog.LoadAsync(http, CacheFile, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
