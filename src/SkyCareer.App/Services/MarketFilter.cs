using SkyCareer.Domain.World;
using SkyCareer.Missions;

namespace SkyCareer.App.Services;

/// <summary>Sortable columns of the offers table.</summary>
public enum OfferColumn { Aircraft, Mission, Distance, Runway, Load, BlockFuel, Deadline, Expires, Payout }

/// <summary>
/// Editable job-search filter of the market page. Kept as a singleton so the filter survives page changes during a
/// session (it is deliberately not stored in the database).
/// </summary>
public sealed class MarketFilter
{
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
