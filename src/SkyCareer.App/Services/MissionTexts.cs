using SkyCareer.Domain.World;
using SkyCareer.Localization;
using SkyCareer.Missions;
using SkyCareer.Missions.Departure;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.App.Services;

/// <summary>Display texts for mission data, shared by the market and mission type pages.</summary>
public static class MissionTexts
{
    public static string Category(string category) => category switch
    {
        "airline" => L.T("Scheduled"),
        "charter" => L.T("Charter"),
        "cargo" => L.T("Cargo"),
        "medical" => L.T("Medical"),
        "bush" => L.T("Bush"),
        _ => category
    };

    public static string Size(AirportSize s) => s switch
    {
        AirportSize.Large => L.T("large"),
        AirportSize.Medium => L.T("medium"),
        AirportSize.Small => L.T("small"),
        AirportSize.Heliport => L.T("heliport"),
        AirportSize.Seaplane => L.T("seaplane base"),
        _ => s.ToString()
    };

    /// <summary>Short summary of a type's destination rule, e.g. "large/medium · large preferred · at least 50 % large".</summary>
    public static string Destinations(DestinationRule rule)
    {
        var parts = new List<string> { rule.Sizes.Count == 0 ? L.T("all sizes") : string.Join("/", rule.Sizes.Select(Size)) };
        var w = rule.SizeWeights;
        if (w.Large > w.Small && w.Large >= w.Medium) parts.Add(L.T("large preferred"));
        else if (w.Small > w.Large) parts.Add(L.T("small preferred"));
        if (rule.MinLargeShare > 0) parts.Add(L.F("at least {0:P0} large", rule.MinLargeShare));
        if (rule.MinCapitalShare > 0) parts.Add(L.F("at least {0:P0} capitals", rule.MinCapitalShare));
        if (rule.CityWeight >= 3) parts.Add(L.T("major cities and capitals strongly preferred"));
        else if (rule.CityWeight > 0) parts.Add(L.T("major cities preferred"));
        if (rule.DistanceInfluence < 1) parts.Add(L.T("city size before distance"));
        return string.Join(" · ", parts);
    }

    /// <summary>"Athens · capital · 3.2M residents" for airports matched to a city of at least 100 000; empty otherwise.</summary>
    public static string City(Airport a)
    {
        if (a.City.Length == 0 || a.CityPopulation < 100_000) return "";
        string pop = a.CityPopulation >= 1_000_000
            ? L.F("{0:0.#}M residents", a.CityPopulation / 1_000_000.0)
            : L.F("{0:N0}k residents", a.CityPopulation / 1000);
        return a.IsCapital ? L.F("{0} · capital · {1}", a.City, pop) : $"{a.City} · {pop}";
    }

    /// <summary>"Athens ★" (capital) or "Munich" for airports serving a city of at least 100 000; empty otherwise.</summary>
    public static string CityShort(Airport a) =>
        a.City.Length == 0 || a.CityPopulation < 100_000 ? "" : a.IsCapital ? $"{a.City} ★" : a.City;

    public static string Phase(FlightPhase? phase) => phase switch
    {
        FlightPhase.Preflight => L.T("Preflight"),
        FlightPhase.TaxiOut => L.T("Taxi out"),
        FlightPhase.TakeoffRoll => L.T("Takeoff roll"),
        FlightPhase.Climb => L.T("Climb"),
        FlightPhase.Cruise => L.T("Cruise"),
        FlightPhase.Descent => L.T("Descent"),
        FlightPhase.Approach => L.T("Approach"),
        FlightPhase.Rollout => L.T("Rollout"),
        FlightPhase.TaxiIn => L.T("Taxi in"),
        FlightPhase.Arrived => L.T("Parked"),
        FlightPhase.Crashed => L.T("Crashed"),
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
        Light.Green => L.T("All set"),
        Light.Yellow => L.T("Ready with remarks"),
        Light.Red => L.T("Not OK"),
        _ => L.T("No simulator data")
    };

    /// <summary>Name for the job-search type filter, marking types the career can't get yet.</summary>
    public static string TypeOption(MissionType type, int reputation) =>
        type.MinReputation > reputation ? L.F("{0} (from reputation {1})", type.DisplayName, type.MinReputation) : type.DisplayName;
}
