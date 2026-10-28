using SkyCareer.Domain.World;
using SkyCareer.Missions;
using SkyCareer.Missions.Departure;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.App.Services;

/// <summary>German display texts for mission data, shared by the market and mission type pages.</summary>
public static class MissionTexts
{
    public static string Category(string category) => category switch
    {
        "airline" => "Linie",
        "charter" => "Charter",
        "cargo" => "Fracht",
        "medical" => "Medizin",
        "bush" => "Busch",
        _ => category
    };

    public static string Size(AirportSize s) => s switch
    {
        AirportSize.Large => "groß",
        AirportSize.Medium => "mittel",
        AirportSize.Small => "klein",
        AirportSize.Heliport => "Hubschrauberlandeplatz",
        AirportSize.Seaplane => "Wasserflugplatz",
        _ => s.ToString()
    };

    /// <summary>Short summary of a type's destination rule, e.g. "groß/mittel · groß bevorzugt · mind. 50 % groß".</summary>
    public static string Destinations(DestinationRule rule)
    {
        var parts = new List<string> { rule.Sizes.Count == 0 ? "alle Größen" : string.Join("/", rule.Sizes.Select(Size)) };
        var w = rule.SizeWeights;
        if (w.Large > w.Small && w.Large >= w.Medium) parts.Add("groß bevorzugt");
        else if (w.Small > w.Large) parts.Add("klein bevorzugt");
        if (rule.MinLargeShare > 0) parts.Add($"mind. {rule.MinLargeShare:P0} groß");
        if (rule.MinCapitalShare > 0) parts.Add($"mind. {rule.MinCapitalShare:P0} Hauptstädte");
        if (rule.CityWeight >= 3) parts.Add("Großstädte und Hauptstädte stark bevorzugt");
        else if (rule.CityWeight > 0) parts.Add("Großstädte bevorzugt");
        if (rule.DistanceInfluence < 1) parts.Add("Stadtgröße vor Distanz");
        return string.Join(" · ", parts);
    }

    /// <summary>"Athen · Hauptstadt · 3,2 Mio. Einw." for airports matched to a city of at least 100 000; empty otherwise.</summary>
    public static string City(Airport a)
    {
        if (a.City.Length == 0 || a.CityPopulation < 100_000) return "";
        string pop = a.CityPopulation >= 1_000_000 ? $"{a.CityPopulation / 1_000_000.0:0.#} Mio. Einw." : $"{a.CityPopulation / 1000:N0} Tsd. Einw.";
        return a.IsCapital ? $"{a.City} · Hauptstadt · {pop}" : $"{a.City} · {pop}";
    }

    /// <summary>"Athen ★" (capital) or "München" for airports serving a city of at least 100 000; empty otherwise.</summary>
    public static string CityShort(Airport a) =>
        a.City.Length == 0 || a.CityPopulation < 100_000 ? "" : a.IsCapital ? $"{a.City} ★" : a.City;

    public static string Phase(FlightPhase? phase) => phase switch
    {
        FlightPhase.Preflight => "Vorbereitung",
        FlightPhase.TaxiOut => "Rollen zum Start",
        FlightPhase.TakeoffRoll => "Startlauf",
        FlightPhase.Climb => "Steigflug",
        FlightPhase.Cruise => "Reiseflug",
        FlightPhase.Descent => "Sinkflug",
        FlightPhase.Approach => "Anflug",
        FlightPhase.Rollout => "Ausrollen",
        FlightPhase.TaxiIn => "Rollen zum Stand",
        FlightPhase.Arrived => "Geparkt",
        FlightPhase.Crashed => "Unfall",
        _ => "–"
    };

    /// <summary>CSS class for a traffic light dot (see app.css).</summary>
    public static string LightClass(Light light) => light switch
    {
        Light.Green => "sc-light sc-light-green",
        Light.Yellow => "sc-light sc-light-yellow",
        Light.Red => "sc-light sc-light-red",
        _ => "sc-light sc-light-unknown"
    };

    public static string LightText(Light light) => light switch
    {
        Light.Green => "Alles bereit",
        Light.Yellow => "Bereit mit Hinweisen",
        Light.Red => "Nicht in Ordnung",
        _ => "Keine Simulatordaten"
    };

    /// <summary>Name for the job-search type filter, marking types the career can't get yet.</summary>
    public static string TypeOption(MissionType type, int reputation) =>
        type.MinReputation > reputation ? $"{type.Name} (ab Reputation {type.MinReputation})" : type.Name;
}
