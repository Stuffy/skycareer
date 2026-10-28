using System.Globalization;
using System.Text.Json;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// The parts of a SimBrief OFP the career needs. Weights are converted to kg regardless of the OFP's unit setting.
/// SimBrief's <c>weights.cargo</c> is bags plus freight (<see cref="CargoKg"/>); <see cref="FreightKg"/> is the freight
/// alone. When the airframe's MZFW/MTOW limits the payload, SimBrief drops bags first (<see cref="BagCount"/> &lt;
/// <see cref="PlannedBagCount"/>) and then passengers.
/// </summary>
public sealed record SimBriefOfp(
    string Origin,
    string Destination,
    string? Alternate,
    string AircraftIcao,
    string Route,
    int Passengers,
    double CargoKg,
    double ZeroFuelWeightKg,
    double BlockFuelKg,
    double TripFuelKg,
    int CruiseAltitudeFt,
    DateTimeOffset GeneratedAt,
    string StaticId,
    double FreightKg = 0,
    int BagCount = 0,
    int PlannedBagCount = 0,
    int PlannedPassengers = 0,
    double PayloadKg = 0,
    double MaxZeroFuelWeightKg = 0,
    double OperatingEmptyKg = 0,
    double PassengerWeightKg = 0,
    double BagWeightKg = 0)
{
    public double BaggageKg => Math.Max(0, CargoKg - FreightKg);
}

/// <summary>
/// Prefill for SimBrief's dispatch page. <see cref="CargoKg"/> is the freight alone: SimBrief adds one bag per
/// passenger with the airframe's bag weight itself. The page ignores custom masses (<c>acdata</c>), so passenger and
/// bag weight come from the airframe and the career settings must match them.
/// <see cref="Airframe"/> is a SimBrief airframe ID that replaces the plain ICAO type.
/// </summary>
public sealed record DispatchRequest(string Origin, string Destination, string AircraftIcao, int Passengers,
    double CargoKg, string? Airline = null, string? FlightNumber = null, string? Callsign = null, string? StaticId = null,
    string? Airframe = null);

/// <summary>
/// Optional SimBrief integration without an API key: the app opens SimBrief's dispatch page prefilled with
/// the mission, the pilot generates the OFP there, and the app fetches the latest OFP by username.
/// </summary>
public sealed class SimBriefClient(HttpClient http)
{
    private const string FetchUrl = "https://www.simbrief.com/api/xml.fetcher.php";
    private const string DispatchUrl = "https://dispatch.simbrief.com/options/custom";

    public static Uri BuildDispatchUri(DispatchRequest r)
    {
        var q = new List<(string, string?)>
        {
            ("orig", r.Origin), ("dest", r.Destination), ("type", string.IsNullOrWhiteSpace(r.Airframe) ? r.AircraftIcao : r.Airframe.Trim()),
            ("pax", r.Passengers.ToString(CultureInfo.InvariantCulture)),
            // SimBrief expects cargo in thousands of the user's weight unit; we always send kg via units=KGS.
            ("cargo", r.CargoKg > 0 ? (r.CargoKg / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) : null),
            ("units", "KGS"),
            ("airline", r.Airline), ("fltnum", r.FlightNumber), ("callsign", r.Callsign), ("static_id", r.StaticId)
        };
        var query = string.Join("&", q.Where(p => !string.IsNullOrWhiteSpace(p.Item2))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2!)}"));
        return new Uri($"{DispatchUrl}?{query}");
    }

    public async Task<SimBriefOfp> FetchLatestAsync(string usernameOrId, CancellationToken ct = default)
    {
        string key = usernameOrId.All(char.IsDigit) ? "userid" : "username";
        var url = $"{FetchUrl}?{key}={Uri.EscapeDataString(usernameOrId)}&json=1";
        using var response = await http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new SimBriefException($"SimBrief returned {(int)response.StatusCode}: {ExtractStatus(body) ?? response.ReasonPhrase}");
        return Parse(body);
    }

    public static SimBriefOfp Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = ExtractStatus(json);
        if (status is not null && !status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            throw new SimBriefException($"SimBrief: {status}");

        bool lbs = Str(root, "params", "units")?.Equals("lbs", StringComparison.OrdinalIgnoreCase) == true;
        double Kg(double v) => lbs ? v * 0.45359237 : v;

        long.TryParse(Str(root, "params", "time_generated"), out var generated);

        int planned = (int)Num(root, "weights", "pax_count");
        int plannedBags = (int)Num(root, "weights", "bag_count");
        return new SimBriefOfp(
            Origin: Str(root, "origin", "icao_code") ?? "",
            Destination: Str(root, "destination", "icao_code") ?? "",
            Alternate: Str(root, "alternate", "icao_code"),
            AircraftIcao: Str(root, "aircraft", "icaocode") ?? "",
            Route: Str(root, "general", "route") ?? "",
            Passengers: Str(root, "weights", "pax_count_actual") is null ? planned : (int)Num(root, "weights", "pax_count_actual"),
            CargoKg: Kg(Num(root, "weights", "cargo")),
            ZeroFuelWeightKg: Kg(Num(root, "weights", "est_zfw")),
            BlockFuelKg: Kg(Num(root, "fuel", "plan_ramp")),
            TripFuelKg: Kg(Num(root, "fuel", "enroute_burn")),
            CruiseAltitudeFt: (int)Num(root, "general", "initial_altitude"),
            GeneratedAt: DateTimeOffset.FromUnixTimeSeconds(generated),
            StaticId: Str(root, "params", "static_id") ?? "",
            FreightKg: Kg(Num(root, "weights", "freight_added")),
            BagCount: Str(root, "weights", "bag_count_actual") is null ? plannedBags : (int)Num(root, "weights", "bag_count_actual"),
            PlannedBagCount: plannedBags,
            PlannedPassengers: planned,
            PayloadKg: Kg(Num(root, "weights", "payload")),
            MaxZeroFuelWeightKg: Kg(Num(root, "weights", "max_zfw")),
            OperatingEmptyKg: Kg(Num(root, "weights", "oew")),
            PassengerWeightKg: Kg(Num(root, "weights", "pax_weight")),
            BagWeightKg: Kg(Num(root, "weights", "bag_weight")));
    }

    private static string? ExtractStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Str(doc.RootElement, "fetch", "status");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement root, string section, string field) =>
        root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object &&
        s.TryGetProperty(field, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    private static double Num(JsonElement root, string section, string field) =>
        double.TryParse(Str(root, section, field), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}

public sealed class SimBriefException(string message) : Exception(message);
