using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;

namespace SkyCareer.Missions;

/// <summary>
/// Criteria for the job search. Used twice: to filter the offers on the market and to steer the generator so a
/// search actually produces matching jobs. Null/empty criteria mean "no restriction".
/// </summary>
public sealed record MissionSearch
{
    public IReadOnlySet<string> MissionTypeIds { get; init; } = new HashSet<string>();
    public double? DistanceMinNm { get; init; }
    public double? DistanceMaxNm { get; init; }
    public int? PaxMin { get; init; }
    public int? PaxMax { get; init; }
    public double? CargoMinKg { get; init; }
    public decimal? PayoutMin { get; init; }

    // Destination airport
    public bool RequirePaved { get; init; }
    public double? MinRunwayM { get; init; }
    public bool RequireLighted { get; init; }
    public bool RequireIls { get; init; }
    public bool ScheduledServiceOnly { get; init; }
    public IReadOnlySet<AirportSize> Sizes { get; init; } = new HashSet<AirportSize>();

    public static MissionSearch Any { get; } = new();

    public bool HasDestinationCriteria =>
        RequirePaved || MinRunwayM is > 0 || RequireLighted || RequireIls || ScheduledServiceOnly || Sizes.Count > 0;

    public bool AcceptsType(MissionType type) => MissionTypeIds.Count == 0 || MissionTypeIds.Contains(type.Id);

    public bool AcceptsDistance(double nm) => (DistanceMinNm is not { } min || nm >= min) && (DistanceMaxNm is not { } max || nm <= max);

    public bool AcceptsDestination(Airport a)
    {
        if (RequirePaved && !a.HasPavedRunway) return false;
        // With "paved" required the paved runway must be long enough; otherwise any runway counts.
        double usableLength = RequirePaved ? a.LongestPavedRunwayM : a.LongestRunwayM;
        if (MinRunwayM is { } minLen && usableLength < minLen) return false;
        if (RequireLighted && !a.HasLightedRunway) return false;
        if (RequireIls && a.HasIls != true) return false;
        if (ScheduledServiceOnly && !a.ScheduledService) return false;
        return Sizes.Count == 0 || Sizes.Contains(a.Size);
    }

    /// <summary>List filter for existing offers. Without destination data, destination criteria count as not met.</summary>
    public bool Matches(MissionContract c, Airport? destination)
    {
        if (MissionTypeIds.Count > 0 && !MissionTypeIds.Contains(c.MissionTypeId)) return false;
        if (!AcceptsDistance(c.DistanceNm)) return false;
        int seated = c.Adults + c.Children;
        if (PaxMin is { } pMin && seated < pMin) return false;
        if (PaxMax is { } pMax && seated > pMax) return false;
        if (CargoMinKg is { } cMin && c.CargoKg < cMin) return false;
        if (PayoutMin is { } pay && c.Payout < pay) return false;
        if (!HasDestinationCriteria) return true;
        return destination is not null && AcceptsDestination(destination);
    }
}
