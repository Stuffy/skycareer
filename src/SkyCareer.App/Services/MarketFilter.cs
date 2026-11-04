using System.Text.Json;
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
    public bool RequirePaved { get; set; }
    public double? MinRunwayM { get; set; }
    public bool RequireLighted { get; set; }
    public bool RequireIls { get; set; }
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
        RequirePaved = RequirePaved,
        MinRunwayM = MinRunwayM,
        RequireLighted = RequireLighted,
        RequireIls = RequireIls,
        ScheduledServiceOnly = ScheduledServiceOnly,
        Sizes = Sizes.ToHashSet()
    };

    /// <summary>Number of active criteria (for the collapsed panel header).</summary>
    public int ActiveCount =>
        new object?[] { AircraftId, DistanceMinNm, DistanceMaxNm, PaxMin, PaxMax, CargoMinKg, PayoutMin, MinRunwayM }.Count(v => v is not null)
        + (MissionTypeIds.Count > 0 ? 1 : 0) + (Sizes.Count > 0 ? 1 : 0)
        + new[] { RequirePaved, RequireLighted, RequireIls, ScheduledServiceOnly }.Count(b => b);

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
