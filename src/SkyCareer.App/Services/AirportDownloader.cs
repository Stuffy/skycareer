using System.IO.Compression;
using SkyCareer.Application;
using SkyCareer.Data;

namespace SkyCareer.App.Services;

/// <summary>
/// Downloads the OurAirports CSVs and the GeoNames city list and imports them (used by the selection screen and the
/// settings page). Without the city list the import still works; destinations just aren't weighted by city size.
/// </summary>
public static class AirportDownloader
{
    public static async Task<int> DownloadAndImportAsync(HttpClient http, CareerService career, CancellationToken ct = default)
    {
        var cities = await TryDownloadCitiesAsync(http, ct);
        using var airports = new StreamReader(await http.GetStreamAsync(OurAirportsImporter.AirportsUrl, ct));
        using var runways = new StreamReader(await http.GetStreamAsync(OurAirportsImporter.RunwaysUrl, ct));
        return await career.ImportAirportsAsync(airports, runways, cities, ct);
    }

    private static async Task<GeoNamesCities?> TryDownloadCitiesAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            // ZipArchive needs a seekable stream; the file is about 3 MB.
            using var buffer = new MemoryStream();
            await using (var s = await http.GetStreamAsync(GeoNamesCities.Url, ct)) await s.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            var entry = zip.GetEntry(GeoNamesCities.EntryName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return GeoNamesCities.Read(reader);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
        {
            return null;
        }
    }
}
