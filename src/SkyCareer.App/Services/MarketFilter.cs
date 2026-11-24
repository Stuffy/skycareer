using System.Text.Json;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions;

namespace SkyCareer.App.Services;

/// <summary>Sortable columns of the offers table.</summary>
public enum OfferColumn { Aircraft, Mission, Distance, Runway, Load, BlockFuel, Deadline, Expires, Payout }

/// <summary>
/// Editable job-search filter of the market page. A singleton for the session; stored per career as JSON
/// (<c>Career.MarketFilterJson</c>) and loaded when the market page opens for another career.
/// </summary>
public sealed class MarketFilter
{
    /// <summary>Career the values were loaded for (a field, so neither JSON nor <see cref="Reset"/> touch it).</summary>
    public Guid? LoadedFor;

    public Guid? AircraftId { get; set; }
    public IReadOnlyCollection<string> MissionTypeIds { get; set; } = [];
    public double? DistanceMinNm { get; set; }
    public double? DistanceMaxNm { get; set; }
    public int? PaxMin { get; set; }
    public int? PaxMax { get; set; }
    public double? CargoMinKg { get; set; }
    public decimal? PayoutMin { get; set; }
    public decimal? PayPerHourMin { get; set; }
    public bool KnownToSimOnly { get; set; }
    /// <summary>List only: a client's id, <see cref="NoClient"/> for jobs without one, null for all.</summary>
    public string? ClientId { get; set; }
    public bool StoryOnly { get; set; }
    public bool PackagesOnly { get; set; }
    public bool DemandOnly { get; set; }

    /// <summary>Value of <see cref="ClientId"/> for "jobs without a client".</summary>
    public const string NoClient = "-";
    public bool RequirePaved { get; set; }
    public double? MinRunwayM { get; set; }
    public bool RequireLighted { get; set; }
    public bool ScheduledServiceOnly { get; set; }
    public IReadOnlyCollection<AirportSize> Sizes { get; set; } = [];
    public OfferColumn SortColumn { get; set; } = OfferColumn.Payout;
    public bool SortDescending { get; set; } = true;
    public bool Expanded { get; set; } = true;

    public MissionSearch ToSearch() => new()
    {
        MissionTypeIds = MissionTypeIds.ToHashSet(),
        DistanceMinNm = DistanceMinNm,
        DistanceMaxNm = DistanceMaxNm,
        PaxMin = PaxMin,
        PaxMax = PaxMax,
        CargoMinKg = CargoMinKg,
        PayoutMin = PayoutMin,
        PayPerHourMin = PayPerHourMin,
        KnownToSimOnly = KnownToSimOnly,
        RequirePaved = RequirePaved,
        MinRunwayM = MinRunwayM,
        RequireLighted = RequireLighted,
        ScheduledServiceOnly = ScheduledServiceOnly,
        Sizes = Sizes.ToHashSet()
    };

    /// <summary>Number of active criteria (for the collapsed panel header).</summary>
    public int ActiveCount =>
        new object?[] { AircraftId, DistanceMinNm, DistanceMaxNm, PaxMin, PaxMax, CargoMinKg, PayoutMin, PayPerHourMin, MinRunwayM, ClientId }.Count(v => v is not null)
        + (MissionTypeIds.Count > 0 ? 1 : 0) + (Sizes.Count > 0 ? 1 : 0)
        + new[] { RequirePaved, RequireLighted, ScheduledServiceOnly, KnownToSimOnly, StoryOnly, PackagesOnly, DemandOnly }.Count(b => b);

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Takes the stored values; missing or unreadable JSON gives the defaults.</summary>
    public void Load(string? json)
    {
        MarketFilter source;
        try { source = string.IsNullOrWhiteSpace(json) ? new MarketFilter() : JsonSerializer.Deserialize<MarketFilter>(json) ?? new MarketFilter(); }
        catch (JsonException) { source = new MarketFilter(); }
        foreach (var p in typeof(MarketFilter).GetProperties().Where(p => p.CanWrite))
            p.SetValue(this, p.GetValue(source));
    }

    /// <summary>The list-only criteria (client, story, packages, demand) for an offer; the search criteria are checked separately.</summary>
    public bool MatchesKind(MissionContract offer, bool isPackage) =>
        (ClientId is null || (ClientId == NoClient ? offer.ClientId is null : offer.ClientId == ClientId))
        && (!StoryOnly || offer.ChainId is not null)
        && (!PackagesOnly || isPackage)
        && (!DemandOnly || offer.DemandEvent is not null);

    /// <summary>Clears all criteria; sorting and the panel state are kept.</summary>
    public void Reset()
    {
        var (column, descending, expanded) = (SortColumn, SortDescending, Expanded);
        var fresh = new MarketFilter();
        foreach (var p in typeof(MarketFilter).GetProperties().Where(p => p.CanWrite))
            p.SetValue(this, p.GetValue(fresh));
        (SortColumn, SortDescending, Expanded) = (column, descending, expanded);
    }
}
