using System.Text.RegularExpressions;

namespace SkyCareer.Domain.Aircraft;

public enum IssueLevel { Error, Warning }

public sealed record ProfileIssue(IssueLevel Level, string Message);

/// <summary>Checks an aircraft profile for values that would break load planning, missions or billing.</summary>
public static partial class AircraftProfileValidator
{
    public static IReadOnlyList<ProfileIssue> Validate(AircraftProfile p)
    {
        var issues = new List<ProfileIssue>();
        void Error(string m) => issues.Add(new(IssueLevel.Error, m));
        void Warn(string m) => issues.Add(new(IssueLevel.Warning, m));

        if (!IdPattern().IsMatch(p.Id)) Error("Die ID darf nur Kleinbuchstaben, Ziffern und _ enthalten (z. B. „c750_citation_x“).");
        if (string.IsNullOrWhiteSpace(p.Name)) Error("Name fehlt.");
        if (p.SimMatch.Count == 0 || p.SimMatch.All(string.IsNullOrWhiteSpace)) Warn("Ohne Sim-Erkennung wird das Flugzeug im Sim nicht automatisch zugeordnet.");

        if (p.OperatingEmptyKg <= 0) Error("Leergewicht muss größer als 0 sein.");
        if (p.MaxTakeoffKg <= p.OperatingEmptyKg) Error("MTOW muss größer als das Leergewicht sein.");
        if (p.MaxZeroFuelKg <= p.OperatingEmptyKg) Error("MZFW muss größer als das Leergewicht sein.");
        if (p.MaxZeroFuelKg > p.MaxTakeoffKg) Error("MZFW darf nicht größer als MTOW sein.");
        if (p.MaxLandingKg > p.MaxTakeoffKg) Error("MLW darf nicht größer als MTOW sein.");
        if (p.MaxLandingKg < p.MaxZeroFuelKg) Warn("MLW unter MZFW ist ungewöhnlich – bitte prüfen.");
        if (p.MaxTakeoffKg > 5_700 && p.MaxZeroFuelKg >= p.MaxTakeoffKg)
            Warn("MZFW gleich MTOW ist bei Flugzeugen dieser Größe unrealistisch – Wert aus Handbuch oder SimBrief übernehmen, sonst passt die Zuladung nicht.");
        if (p.MaxFuelKg <= 0) Error("Tankkapazität muss größer als 0 sein.");

        if (p.CruiseKtas <= 0) Error("Reisegeschwindigkeit fehlt – ohne sie gibt es keine Treibstoffplanung.");
        if (p.FuelBurnKgPerHour <= 0) Error("Verbrauch pro Stunde fehlt – ohne ihn gibt es keine Treibstoffplanung.");
        if (p.PassengerSeats <= 0 && p.MaxCargoKg <= 0) Warn("Weder Sitze noch Frachtkapazität – es passen kaum Missionen.");
        if (p.CrewCount < 1) Error("Mindestens ein Pilot (Crew) ist nötig.");
        else if (p.CrewCount < p.MinimumPilots)
            Warn($"Flugzeuge dieser Klasse (Jet, über 5,7 t MTOW oder mehr als 9 Sitze) fliegen mit zwei Piloten – es werden {p.MinimumPilots} eingeplant. "
                 + $"Für Single-Pilot-zugelassene Typen den Tag „{AircraftProfile.SinglePilotTag}“ setzen.");
        if (p.Surfaces.Count == 0) Error("Mindestens eine Pistenoberfläche angeben.");

        if (p.Loading == LoadingMethod.SimConnectStations)
        {
            if (p.Stations.Count == 0) Error("Für das Laden per SimConnect werden Payload-Stationen benötigt.");
            if (p.Stations.GroupBy(s => s.Index).Any(g => g.Count() > 1)) Error("Stationsnummern müssen eindeutig sein.");
            if (p.Stations.Any(s => s.Index < 1)) Error("Stationsnummern beginnen bei 1.");
            int seats = p.Stations.Where(s => s.Kind == StationKind.Passengers).Sum(s => s.Seats);
            if (seats != p.PassengerSeats)
                Warn($"Die Passagierstationen haben {seats} Sitze, das Profil {p.PassengerSeats}.");
            if (p.MaxCargoKg > 0 && !p.Stations.Any(s => s.Kind is StationKind.Baggage or StationKind.Cargo))
                Warn("Frachtkapazität ohne Gepäck-/Frachtstation – Gepäck und Fracht landen dann nicht im Sim.");
            if (!p.CrewIncludedInEmptyWeight && !p.Stations.Any(s => s.Kind == StationKind.Crew))
                Warn("Keine Crew-Station: Das Crewgewicht wird nicht in den Sim geschrieben.");
        }

        if (p.Economics.PurchasePriceNew <= 0 || p.Economics.RentPerBlockHour <= 0 || p.Economics.LeasePerMonth <= 0)
            Warn("Preise fehlen – das Flugzeug kann dann nicht sinnvoll gekauft, geleast oder gemietet werden.");
        return issues;
    }

    public static bool IsValid(AircraftProfile p) => Validate(p).All(i => i.Level != IssueLevel.Error);

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex IdPattern();
}

/// <summary>
/// Fills values the simulator doesn't expose (performance, prices) from the most similar known profile,
/// scaled by take-off mass. Results are estimates the user should review.
/// </summary>
public static class AircraftProfileEstimator
{
    /// <summary>
    /// MZFW when only empty and max gross weight are known. Light aircraft (≤ 5.7 t) usually have none (= MTOW);
    /// otherwise empty weight + two pilots + 100 kg per seat, at least 15 % of the useful load (Citation X: ~11.1 t,
    /// real 11.07 t). Deliberately on the low side so planned loads stay inside the real limit.
    /// </summary>
    public static double EstimateMaxZeroFuelKg(double emptyKg, double maxTakeoffKg, int seats)
    {
        if (maxTakeoffKg <= 5_700) return Math.Round(maxTakeoffKg);
        double bySeats = emptyKg + 170 + seats * 100;
        double byShare = emptyKg + 0.15 * (maxTakeoffKg - emptyKg);
        return Math.Round(Math.Min(maxTakeoffKg, Math.Max(bySeats, byShare)));
    }

    /// <summary>MLW: equal to MTOW for light aircraft, about 90 % of it for larger ones (Citation X 88 %, A320 84 %).</summary>
    public static double EstimateMaxLandingKg(double maxTakeoffKg) =>
        Math.Round(maxTakeoffKg <= 5_700 ? maxTakeoffKg : maxTakeoffKg * 0.9);

    public static AircraftProfile? MostSimilar(AircraftProfile target, IEnumerable<AircraftProfile> known) =>
        known.Where(k => k.Id != target.Id && k.MaxTakeoffKg > 0)
            .OrderBy(k => (k.FuelType == target.FuelType ? 0 : 1) + (k.HasTag("helicopter") == target.HasTag("helicopter") ? 0 : 2))
            .ThenBy(k => Math.Abs(Math.Log(k.MaxTakeoffKg / Math.Max(1, target.MaxTakeoffKg))))
            .FirstOrDefault();

    /// <summary>Copies missing performance and economic values from <paramref name="reference"/>, scaled by MTOW.</summary>
    public static AircraftProfile FillFrom(AircraftProfile target, AircraftProfile reference, bool overwrite = false)
    {
        double f = target.MaxTakeoffKg > 0 && reference.MaxTakeoffKg > 0 ? target.MaxTakeoffKg / reference.MaxTakeoffKg : 1;
        decimal fm = (decimal)f;
        double Pick(double current, double estimate) => overwrite || current <= 0 ? Math.Round(estimate) : current;
        // Estimated prices rounded to whole 10 € (decimal can't round to negative digits).
        decimal PickM(decimal current, decimal estimate) => overwrite || current <= 0 ? Math.Round(estimate / 10) * 10 : current;

        var e = target.Economics;
        var r = reference.Economics;
        return target with
        {
            CruiseKtas = Pick(target.CruiseKtas, reference.CruiseKtas),
            FuelBurnKgPerHour = Pick(target.FuelBurnKgPerHour, reference.FuelBurnKgPerHour * f),
            TaxiFuelKg = Pick(target.TaxiFuelKg, reference.TaxiFuelKg * f),
            MinRunwayM = Pick(target.MinRunwayM, reference.MinRunwayM),
            MaxCargoKg = Pick(target.MaxCargoKg, reference.MaxCargoKg * f),
            Economics = new AircraftEconomics
            {
                PurchasePriceNew = PickM(e.PurchasePriceNew, r.PurchasePriceNew * fm),
                LeasePerMonth = PickM(e.LeasePerMonth, r.LeasePerMonth * fm),
                RentPerBlockHour = PickM(e.RentPerBlockHour, r.RentPerBlockHour * fm),
                MaintenancePerFlightHour = PickM(e.MaintenancePerFlightHour, r.MaintenancePerFlightHour * fm),
                InsurancePerMonth = PickM(e.InsurancePerMonth, r.InsurancePerMonth * fm)
            }
        };
    }

    /// <summary>Lowercase id from a name, e.g. "Citation X Winglets" → "citation_x_winglets".</summary>
    public static string Slug(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_').ToArray();
        var slug = string.Join("_", new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "aircraft" : slug;
    }

    /// <summary>
    /// ICAO type from MSFS's ATC MODEL, which comes as e.g. "$$:C750" or "TT:ATCCOM.AC_MODEL_C172.0.text".
    /// Returns null if nothing usable is found.
    /// </summary>
    public static string? IcaoFromAtcModel(string atcModel)
    {
        var s = atcModel.Trim();
        int i = s.IndexOf("AC_MODEL", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            s = s[(i + "AC_MODEL".Length)..].TrimStart('_', ' ');
            int dot = s.IndexOf('.');
            if (dot > 0) s = s[..dot];
        }
        else if (s.StartsWith("$$:")) s = s[3..];
        s = s.Trim().ToUpperInvariant();
        return s.Length is >= 2 and <= 4 && s.All(char.IsLetterOrDigit) ? s : null;
    }
}
