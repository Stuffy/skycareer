using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkyCareer.Integrations.Gsx;

/// <summary>Receipt folders GSX writes below its Receipts directory.</summary>
public enum GsxReceiptKind { Handling, Catering, Fuel, Bus }

/// <summary>One GSX Pro receipt (JSON next to a printable HTML invoice), with the total converted to euros.</summary>
public sealed record GsxReceipt(
    string ReceiptId,
    GsxReceiptKind Kind,
    string Icao,
    string? Tail,
    string Operator,
    string Title,
    DateTime CreatedUtc,
    decimal AmountEur,
    IReadOnlyList<string> Lines,
    string? HtmlPath);

/// <summary>
/// Reads the receipts GSX Pro writes to <c>%APPDATA%\Virtuali\GSX\Receipts\&lt;Kind&gt;\yyyyMMddTHHmmssZ_ICAO_TAIL.json</c>.
/// Amounts are text in the local currency followed by a USD equivalent (<c>"£147.62 ~$ 196.50"</c>); euros are taken
/// as they are, anything else is converted from the USD part with the exchange rates GSX caches itself.
/// </summary>
public static partial class GsxReceiptReader
{
    /// <summary>Euros per US dollar when GSX's rate cache is missing.</summary>
    public const decimal FallbackEurPerUsd = 0.92m;

    /// <summary>The SKYCAREER_GSX_RECEIPTS environment variable points the reader at another folder (testing).</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("SKYCAREER_GSX_RECEIPTS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtuali", "GSX", "Receipts");

    public static string DefaultFxPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtuali", "Couatl", "Cache", "fx-rates-v2.json");

    /// <summary>All readable receipts created after <paramref name="sinceUtc"/>, oldest first. Unreadable files are skipped.</summary>
    public static IReadOnlyList<GsxReceipt> Scan(string root, DateTime sinceUtc, decimal? eurPerUsd = null)
    {
        if (!Directory.Exists(root)) return [];
        decimal rate = eurPerUsd ?? LoadEurPerUsd(DefaultFxPath) ?? FallbackEurPerUsd;
        var receipts = new List<GsxReceipt>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var created = CreatedUtc(file);
            if (created <= sinceUtc) continue;
            if (TryRead(file, rate) is { } receipt) receipts.Add(receipt);
        }
        return receipts.OrderBy(r => r.CreatedUtc).ToList();
    }

    /// <summary>Parses one receipt file; null when it is incomplete (GSX may still be writing it) or has no usable total.</summary>
    public static GsxReceipt? TryRead(string jsonPath, decimal eurPerUsd)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var r = doc.RootElement;
            string? id = Str(r, "receiptId");
            string? icao = Str(r, "icao");
            if (id is null || icao is null || ParseAmountEur(Str(r, "total"), eurPerUsd) is not { } total) return null;

            var lines = new List<string>();
            if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                {
                    string qty = Str(item, "qty") is { Length: > 0 } q && q != "1" ? $" ({q})" : "";
                    lines.Add($"{Str(item, "description")?.Replace('\n', ' ')}{qty}: {Str(item, "amount")}");
                }
            if (r.TryGetProperty("taxes", out var taxes) && taxes.ValueKind == JsonValueKind.Array)
                foreach (var tax in taxes.EnumerateArray())
                    lines.Add($"{Str(tax, "label")} {Str(tax, "rate")}: {Str(tax, "amount")}");

            string html = Path.ChangeExtension(jsonPath, ".html");
            return new GsxReceipt(id, KindOf(jsonPath), icao.ToUpperInvariant(), Str(r, "tail"), Str(r, "operator") ?? "", Title(Str(r, "title")),
                CreatedUtc(jsonPath), total, lines, File.Exists(html) ? html : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>"€60.09 ~$ 67.40"</c> → 60.09; <c>"£147.62 ~$ 196.50"</c> → 196.50 × rate; <c>"$163.75"</c> → 163.75 × rate.
    /// Null when no amount can be read.
    /// </summary>
    public static decimal? ParseAmountEur(string? text, decimal eurPerUsd)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var local = LocalAmount().Match(text);
        if (local.Success && local.Groups["sym"].Value.Trim() is "€" or "EUR")
            return Number(local.Groups["num"].Value);
        var usd = UsdAmount().Match(text);
        if (usd.Success) return Math.Round(Number(usd.Groups["num"].Value) * eurPerUsd, 2);
        if (local.Success && local.Groups["sym"].Value.Trim() is "$" or "US$" or "USD")
            return Math.Round(Number(local.Groups["num"].Value) * eurPerUsd, 2);
        return null;
    }

    /// <summary>Euros per US dollar from GSX's cached exchange rates (base USD), or null.</summary>
    public static decimal? LoadEurPerUsd(string fxPath)
    {
        try
        {
            if (!File.Exists(fxPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(fxPath));
            return doc.RootElement.TryGetProperty("rates", out var rates) && rates.TryGetProperty("EUR", out var eur) && eur.TryGetDecimal(out var v) && v > 0
                ? v
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GsxReceiptKind KindOf(string path) => Path.GetFileName(Path.GetDirectoryName(path)) switch
    {
        "Fuel" => GsxReceiptKind.Fuel,
        "Catering" => GsxReceiptKind.Catering,
        "PassengerBus" => GsxReceiptKind.Bus,
        _ => GsxReceiptKind.Handling
    };

    /// <summary>From the file name prefix (<c>20261029T135459Z_…</c>), else the file time.</summary>
    private static DateTime CreatedUtc(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int cut = name.IndexOf('_');
        if (cut > 0 && DateTime.TryParseExact(name[..cut], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
            return t;
        return File.GetLastWriteTimeUtc(path);
    }

    /// <summary>"GROUND HANDLING RECEIPT" → "Ground Handling".</summary>
    private static string Title(string? title)
    {
        string t = (title ?? "").Trim();
        if (t.EndsWith(" RECEIPT", StringComparison.OrdinalIgnoreCase)) t = t[..^" RECEIPT".Length];
        return t.Length == 0 ? "Beleg" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToLowerInvariant());
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal Number(string s) => decimal.Parse(s.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\s*(?<sym>[^\d\-~]*?)\s*(?<num>-?\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex LocalAmount();

    [GeneratedRegex(@"~\s*\$\s*(?<num>-?\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex UsdAmount();
}
