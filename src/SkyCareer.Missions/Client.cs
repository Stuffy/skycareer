using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions;

/// <summary>Translated texts of a client, a story chain or one of its steps.</summary>
public sealed record ClientText(string? Name = null, string? Description = null, string? Title = null, string? Briefing = null);

/// <summary>
/// A recurring client as defined in content/clients/*.json: a company that hands out jobs of certain mission types, keeps
/// its own standing with the airline and may have story chains – short series of jobs that tell a story.
/// </summary>
public sealed record Client
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyDictionary<string, ClientText> Texts { get; init; } = new Dictionary<string, ClientText>();
    /// <summary>Mission types the client hands out.</summary>
    public IReadOnlyList<string> MissionTypes { get; init; } = [];
    /// <summary>ISO countries the client works in (origin or destination); empty = everywhere.</summary>
    public IReadOnlyList<string> Countries { get; init; } = [];
    public IReadOnlyList<StoryChain> Chains { get; init; } = [];

    public string DisplayDescription => Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Description) ? t.Description : Description;

    /// <summary>Initials for the client's badge ("Kestrel Mining" → "KM").</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>Whether the client could have given a job of <paramref name="type"/> between these airports.</summary>
    public bool Offers(MissionType type, Airport origin, Airport destination) =>
        MissionTypes.Contains(type.Id, StringComparer.OrdinalIgnoreCase)
        && (Countries.Count == 0 || Countries.Contains(origin.Country, StringComparer.OrdinalIgnoreCase) || Countries.Contains(destination.Country, StringComparer.OrdinalIgnoreCase));
}

/// <summary>A short series of jobs for one client, unlocked from a standing and flown step by step.</summary>
public sealed record StoryChain
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyDictionary<string, ClientText> Texts { get; init; } = new Dictionary<string, ClientText>();
    public int MinStanding { get; init; }
    public IReadOnlyList<StoryStep> Steps { get; init; } = [];
    /// <summary>Standing the client adds when the last step is flown.</summary>
    public int CompletionStanding { get; init; } = 10;

    public string DisplayTitle => Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Title) ? t.Title : Title;
}

/// <summary>
/// One job of a story chain: a mission type with an optional fixed destination (ICAO) or a destination rule, and a bonus
/// share on the payout. The cargo kind replaces the type's when given.
/// </summary>
public sealed record StoryStep
{
    public required string Title { get; init; }
    public string Briefing { get; init; } = "";
    public IReadOnlyDictionary<string, ClientText> Texts { get; init; } = new Dictionary<string, ClientText>();
    public required string MissionType { get; init; }
    public string? Destination { get; init; }
    public IReadOnlyList<AirportSize> Sizes { get; init; } = [];
    public double? MinNm { get; init; }
    public double? MaxNm { get; init; }
    public string? CargoKind { get; init; }
    public decimal Bonus { get; init; } = 0.2m;

    public string DisplayTitle => Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Title) ? t.Title : Title;
    public string DisplayBriefing => Texts.TryGetValue(L.Language, out var t) && !string.IsNullOrEmpty(t.Briefing) ? t.Briefing : Briefing;

    public bool Allows(Airport origin, Airport a)
    {
        if (Destination is { } fixedIdent) return string.Equals(a.Ident, fixedIdent, StringComparison.OrdinalIgnoreCase);
        double d = origin.Position.DistanceNmTo(a.Position);
        return (Sizes.Count == 0 || Sizes.Contains(a.Size)) && d >= (MinNm ?? 0) && d <= (MaxNm ?? double.MaxValue);
    }
}

public enum ClientTier { Bronze, Silver, Gold }

/// <summary>Standing rules shared by the service, the settlement page and the client page.</summary>
public static class ClientStandings
{
    public const int Start = 10;
    public const int SilverFrom = 40, GoldFrom = 70;
    /// <summary>Pay bonus per tier above bronze.</summary>
    public const decimal PayPerTier = 0.05m;
    /// <summary>Share of offers that come from a client when one fits.</summary>
    public const double ClientShare = 0.6;
    /// <summary>A series: flights to fly for the client within <see cref="SeriesDays"/> game days, bonus share on their payouts.</summary>
    public const int SeriesDays = 7;
    public const decimal SeriesBonus = 0.2m;
    public const int SeriesStanding = 5;

    public static ClientTier Tier(int standing) => standing >= GoldFrom ? ClientTier.Gold : standing >= SilverFrom ? ClientTier.Silver : ClientTier.Bronze;

    public static decimal PayFactor(int standing) => 1 + PayPerTier * (int)Tier(standing);

    /// <summary>Flights a series of this client asks for: more the better the airline is known.</summary>
    public static int SeriesTarget(int standing) => Tier(standing) == ClientTier.Gold ? 5 : 4;

    /// <summary>How a flight changes the client's standing.</summary>
    public static int Change(Domain.Career.FlightOutcome outcome, int score) => outcome switch
    {
        Domain.Career.FlightOutcome.Completed => 2 + (score >= 85 ? 1 : 0),
        Domain.Career.FlightOutcome.Late => -3,
        Domain.Career.FlightOutcome.Diverted => -5,
        Domain.Career.FlightOutcome.Crashed => -20,
        _ => 0
    };

    public const int CancellationStanding = -5;

    public static string TierText(ClientTier tier) => tier switch
    {
        ClientTier.Gold => L.T("Gold"),
        ClientTier.Silver => L.T("Silver"),
        _ => L.T("Bronze")
    };
}
