using SkyCareer.Integrations.Gsx;

namespace SkyCareer.Integrations.Tests;

public class GsxReceiptTests
{
    // Shortened copies of real GSX Pro receipts (logoDataUri removed).
    private const string Handling = """
        {"version": 1, "operator": "ABM Handling", "logoDataUri": "data:image/png;base64,AAAA", "title": "GROUND HANDLING RECEIPT",
         "receiptId": "GSX-20261029T135459Z-EGGP", "icao": "EGGP", "tail": "VH-UWH", "aircraftType": "C750",
         "items": [{"description": "Pushback service\n(16 t MTOW)", "qty": "1", "unitPrice": "", "amount": "£123.01 ~$ 163.75"}],
         "subtotal": "£123.01 ~$ 163.75", "taxes": [{"label": "VAT (United Kingdom)", "rate": "20%", "amount": "£24.60 ~$ 32.75", "reason": ""}],
         "total": "£147.62 ~$ 196.50"}
        """;

    private const string Fuel = """
        {"version": 1, "operator": "Total Energies", "title": "FUEL DELIVERY RECEIPT", "receiptId": "GSX-20261027T135739Z-EGGW",
         "icao": "EGGW", "tail": "VH-UWH",
         "items": [{"description": "Jet-A1 fuel", "qty": "386 gal 2,589 lb 1,174 kg", "unitPrice": "£3.93/gal ~$5.26/gal", "amount": "£1,518.15 ~$ 2,031.35"}],
         "taxes": [], "total": "£1,561.46 ~$ 2,089.30"}
        """;

    private const string Boarding = """
        {"version": 1, "operator": "DLH", "title": "GROUND HANDLING RECEIPT", "receiptId": "GSX-20261012T092114Z-EDDH", "icao": "eddh",
         "items": [{"description": "Passenger boarding", "qty": "8 pax", "unitPrice": "€6.31/pax ~$7.08/pax", "amount": "€50.50 ~$ 56.64"}],
         "taxes": [{"label": "VAT (Germany)", "rate": "19%", "amount": "€9.59 ~$ 10.76", "reason": ""}], "total": "€60.09 ~$ 67.40"}
        """;

    [Theory]
    [InlineData("€60.09 ~$ 67.40", 60.09)]
    [InlineData("£147.62 ~$ 196.50", 180.78)]       // 196.50 $ × 0.92
    [InlineData("£1,561.46 ~$ 2,089.30", 1922.16)]  // thousands separator
    [InlineData("$163.75", 150.65)]                 // US airport: no conversion part
    public void Amounts_are_converted_to_euros(string text, double expected) =>
        Assert.Equal((decimal)expected, GsxReceiptReader.ParseAmountEur(text, 0.92m));

    [Fact]
    public void Unreadable_amounts_are_null()
    {
        Assert.Null(GsxReceiptReader.ParseAmountEur("", 0.92m));
        Assert.Null(GsxReceiptReader.ParseAmountEur("¥1200", 0.92m));
    }

    [Fact]
    public void Scan_reads_new_receipts_and_skips_broken_or_old_ones()
    {
        string root = Path.Combine(Path.GetTempPath(), $"skycareer-gsx-{Guid.NewGuid():N}");
        try
        {
            Write(root, "Handling", "20261029T135459Z_EGGP_VH-UWH", Handling, html: true);
            Write(root, "Fuel", "20261027T135739Z_EGGW_VH-UWH", Fuel);
            Write(root, "Handling", "20261012T092114Z_EDDH_VH-UWH", Boarding);
            Write(root, "Catering", "20261029T140000Z_EGGP_VH-UWH", "{\"receiptId\": \"GSX-half"); // still being written

            var all = GsxReceiptReader.Scan(root, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), eurPerUsd: 0.9m);
            Assert.Equal(["GSX-20261027T135739Z-EGGW", "GSX-20261029T135459Z-EGGP"], all.Select(r => r.ReceiptId));

            var fuel = all[0];
            Assert.Equal(GsxReceiptKind.Fuel, fuel.Kind);
            Assert.Equal(new DateTime(2026, 10, 27, 13, 57, 39, DateTimeKind.Utc), fuel.CreatedUtc);
            Assert.Equal(1880.37m, fuel.AmountEur);
            Assert.Null(fuel.HtmlPath);

            var handling = all[1];
            Assert.Equal("EGGP", handling.Icao);
            Assert.Equal("ABM Handling", handling.Operator);
            Assert.Equal("Ground Handling", handling.Title);
            Assert.NotNull(handling.HtmlPath);
            Assert.Contains(handling.Lines, l => l.StartsWith("Pushback service (16 t MTOW)"));
            Assert.Contains(handling.Lines, l => l.StartsWith("VAT (United Kingdom) 20%"));

            var euro = Assert.Single(GsxReceiptReader.Scan(root, DateTime.MinValue, 0.9m), r => r.ReceiptId.EndsWith("EDDH"));
            Assert.Equal("EDDH", euro.Icao);
            Assert.Equal(60.09m, euro.AmountEur);

            Assert.Empty(GsxReceiptReader.Scan(Path.Combine(root, "missing"), DateTime.MinValue, 0.9m));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Exchange_rate_comes_from_the_gsx_cache()
    {
        string file = Path.Combine(Path.GetTempPath(), $"skycareer-fx-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, """{"base": "USD", "rates": {"EUR": 0.89648, "GBP": 0.75123}}""");
            Assert.Equal(0.89648m, GsxReceiptReader.LoadEurPerUsd(file));
            File.WriteAllText(file, "{broken");
            Assert.Null(GsxReceiptReader.LoadEurPerUsd(file));
            Assert.Null(GsxReceiptReader.LoadEurPerUsd(file + ".missing"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void Write(string root, string kind, string name, string json, bool html = false)
    {
        string dir = Path.Combine(root, kind);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".json"), json);
        if (html) File.WriteAllText(Path.Combine(dir, name + ".html"), "<html>receipt</html>");
    }
}
