using SkyCareer.Domain.Aircraft;
using SkyCareer.Sim;

namespace SkyCareer.App.Services;

/// <summary>
/// Turns what the live aircraft reports into a starting point for a profile. Values the sim doesn't expose
/// (MZFW, MLW, fuel burn, cargo capacity, prices) are estimated from the most similar known profile and
/// must be reviewed in the editor.
/// </summary>
public static class ProfileDraftBuilder
{
    public static AircraftProfile Build(AircraftSnapshot s, IEnumerable<AircraftProfile> known)
    {
        var design = s.Design;
        // Jet-A weighs ~3.04 kg/gal, AvGas ~2.72 kg/gal; the engine type is the more reliable hint.
        var fuelType = design?.EngineType == SimEngineType.Piston || (design is null && s.FuelKgPerGallon is > 0 and < 2.9)
            ? FuelType.AvGas : FuelType.JetA;

        var stations = s.Stations.Select(st =>
        {
            var kind = GuessKind(st.Name);
            int seats = kind is StationKind.Passengers or StationKind.Crew ? SeatsFromName(st.Name) ?? Math.Max(1, (int)Math.Round(st.WeightKg / 84)) : 0;
            // Hold capacity isn't exposed by the sim; MaxKg stays 0 until filled in.
            return new PayloadStation(st.Index, st.Name, kind, seats);
        }).ToList();

        string? icao = AircraftProfileEstimator.IcaoFromAtcModel(s.AtcModel);
        int paxSeats = stations.Where(x => x.Kind == StationKind.Passengers).Sum(x => x.Seats);
        bool helicopter = design?.IsHelicopter == true;

        var draft = new AircraftProfile
        {
            Id = AircraftProfileEstimator.Slug(s.Title),
            Name = s.Title,
            IcaoType = icao ?? "ZZZZ",
            SimMatch = icao is null ? [s.Title] : [s.Title, icao],
            OperatingEmptyKg = Math.Round(s.EmptyWeightKg),
            // The sim only reports the max gross weight; MZFW and MLW are estimated (check against the POH or SimBrief).
            MaxZeroFuelKg = AircraftProfileEstimator.EstimateMaxZeroFuelKg(s.EmptyWeightKg, s.MaxGrossWeightKg, paxSeats),
            MaxTakeoffKg = Math.Round(s.MaxGrossWeightKg),
            MaxLandingKg = AircraftProfileEstimator.EstimateMaxLandingKg(s.MaxGrossWeightKg),
            MaxFuelKg = Math.Round(s.TankCapacityGallons * s.FuelKgPerGallon),
            FuelType = fuelType,
            PassengerSeats = paxSeats,
            CrewCount = Math.Max(1, stations.Where(x => x.Kind == StationKind.Crew).Sum(x => x.Seats)),
            CruiseKtas = Math.Round(design?.EstimatedCruiseKtas ?? 0),
            Surfaces = helicopter
                ? [RunwaySurface.Paved, RunwaySurface.Grass, RunwaySurface.Gravel, RunwaySurface.Dirt, RunwaySurface.Snow]
                : [RunwaySurface.Paved],
            Tags = GuessTags(design, paxSeats, stations),
            Loading = s.Stations.Count > 0 ? LoadingMethod.SimConnectStations : LoadingMethod.Manual,
            Stations = stations
        };

        return AircraftProfileEstimator.MostSimilar(draft, known) is { } reference
            ? AircraftProfileEstimator.FillFrom(draft, reference)
            : draft;
    }

    public static StationKind GuessKind(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("pilot") || n.Contains("crew") || n.Contains("captain") || n.Contains("officer")) return StationKind.Crew;
        if (n.Contains("baggage") || n.Contains("luggage") || n.Contains("bag")) return StationKind.Baggage;
        if (n.Contains("cargo") || n.Contains("freight") || n.Contains("hold") || n.Contains("pod")) return StationKind.Cargo;
        return StationKind.Passengers;
    }

    /// <summary>Station names like "PAX 1" are single seats; "Rear passengers (2)" names its count.</summary>
    private static int? SeatsFromName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+)\)");
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^\s*(pax|passenger|seat)\s*\d+\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 1 : null;
    }

    private static List<string> GuessTags(AircraftDesignInfo? d, int paxSeats, List<PayloadStation> stations)
    {
        var tags = new List<string>();
        if (paxSeats > 0) tags.Add("pax");
        if (stations.Any(s => s.Kind == StationKind.Cargo)) tags.Add("cargo");
        if (d is not null)
        {
            if (d.IsHelicopter) tags.AddRange(["helicopter", "turbine"]);
            else
            {
                tags.Add(d.EngineType switch { SimEngineType.Jet => "jet", SimEngineType.Turboprop => "turboprop", SimEngineType.Piston => "piston", _ => "aircraft" });
                tags.Add(d.EngineCount > 1 ? "multi_engine" : "single_engine");
            }
        }
        return tags;
    }
}
