using System.Text.RegularExpressions;
using SkyCareer.Localization;

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

        if (!IdPattern().IsMatch(p.Id)) Error(L.T("The ID may only contain lowercase letters, digits and _ (e.g. “c750_citation_x”)."));
        if (string.IsNullOrWhiteSpace(p.Name)) Error(L.T("Name is missing."));
        if (p.SimMatch.Count == 0 || p.SimMatch.All(string.IsNullOrWhiteSpace)) Warn(L.T("Without sim matching the aircraft won't be recognised in the sim automatically."));

        if (p.OperatingEmptyKg <= 0) Error(L.T("Empty weight must be greater than 0."));
        if (p.MaxTakeoffKg <= p.OperatingEmptyKg) Error(L.T("MTOW must be greater than the empty weight."));
        if (p.MaxZeroFuelKg <= p.OperatingEmptyKg) Error(L.T("MZFW must be greater than the empty weight."));
        if (p.MaxZeroFuelKg > p.MaxTakeoffKg) Error(L.T("MZFW must not exceed MTOW."));
        if (p.MaxLandingKg > p.MaxTakeoffKg) Error(L.T("MLW must not exceed MTOW."));
        if (p.MaxLandingKg < p.MaxZeroFuelKg) Warn(L.T("MLW below MZFW is unusual – please check."));
        if (p.MaxTakeoffKg > 5_700 && p.MaxZeroFuelKg >= p.MaxTakeoffKg)
            Warn(L.T("MZFW equal to MTOW is unrealistic for aircraft of this size – take the value from the manual or SimBrief, otherwise the payload won't be right."));
        if (p.MaxFuelKg <= 0) Error(L.T("Fuel capacity must be greater than 0."));

        if (p.CruiseKtas <= 0) Error(L.T("Cruise speed is missing – fuel planning is impossible without it."));
        if (p.FuelBurnKgPerHour <= 0) Error(L.T("Fuel burn per hour is missing – fuel planning is impossible without it."));
        if (p.PassengerSeats <= 0 && p.MaxCargoKg <= 0) Warn(L.T("Neither seats nor cargo capacity – hardly any missions will fit."));
        if (p.CrewCount < 1) Error(L.T("At least one pilot (crew) is required."));
        else if (p.CrewCount < p.MinimumPilots)
            Warn(L.F("Aircraft of this class (jet, MTOW above 5.7 t or more than 9 seats) are flown by two pilots – {0} will be planned. ", p.MinimumPilots)
                 + L.F("For types certified for single-pilot operation, set the tag “{0}”.", AircraftProfile.SinglePilotTag));
        if (p.Surfaces.Count == 0) Error(L.T("Specify at least one runway surface."));

        if (p.Loading == LoadingMethod.SimConnectStations)
        {
            if (p.Stations.Count == 0) Error(L.T("Loading via SimConnect requires payload stations."));
            if (p.Stations.GroupBy(s => s.Index).Any(g => g.Count() > 1)) Error(L.T("Station numbers must be unique."));
            if (p.Stations.Any(s => s.Index < 1)) Error(L.T("Station numbers start at 1."));
            int seats = p.Stations.Where(s => s.Kind == StationKind.Passengers).Sum(s => s.Seats);
            if (seats != p.PassengerSeats)
                Warn(L.F("The passenger stations have {0} seats, the profile {1}.", seats, p.PassengerSeats));
            if (p.MaxCargoKg > 0 && !p.Stations.Any(s => s.Kind is StationKind.Baggage or StationKind.Cargo))
                Warn(L.T("Cargo capacity without a baggage/cargo station – baggage and cargo then won't reach the sim."));
            if (!p.CrewIncludedInEmptyWeight && !p.Stations.Any(s => s.Kind == StationKind.Crew))
                Warn(L.T("No crew station: the crew weight is not written to the sim."));
        }

        if (p.Economics.PurchasePriceNew <= 0 || p.Economics.RentPerBlockHour <= 0 || p.Economics.LeasePerMonth <= 0)
            Warn(L.T("Prices are missing – the aircraft can't reasonably be bought, leased or rented."));
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
