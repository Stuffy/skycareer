using SkyCareer.Domain;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Generation;

/// <summary>
/// Round trips from the aircraft's airfield over 2–4 waypoints and back (sightseeing, aerial survey, pipeline patrol).
/// There is no point-of-interest data, so the waypoints are the towns and airfields around: an airport's city (or its
/// name) labels the point. The tour's length is the job's distance, so pay, deadline and fuel plan work as for any job.
/// </summary>
public sealed class SightseeingMissionGenerator : IMissionGenerator
{
    public string Name => "sightseeing";

    public MissionOffer? TryGenerate(GenerationContext ctx)
    {
        var (type, aircraft, origin, airports, now, rng) = ctx;
        var tour = type.Tour ?? new TourRule();
        double range = RequirementEvaluator.EstimatedRangeNm(aircraft);
        double maxOut = Math.Min(type.Distance.MaxNm, range * 0.4);

        // One waypoint per town: the biggest airport of each place.
        var points = airports
            .Where(a => a.Ident != origin.Ident && !a.IsMilitary)
            .Select(a => (Airport: a, Dist: origin.Position.DistanceNmTo(a.Position)))
            .Where(x => x.Dist >= type.Distance.MinNm && x.Dist <= maxOut)
            .GroupBy(x => Label(x.Airport))
            .Select(g => g.OrderByDescending(x => x.Airport.Size).First())
            .ToList();
        int count = rng.Next(Math.Max(1, tour.MinWaypoints), Math.Max(tour.MinWaypoints, tour.MaxWaypoints) + 1);
        if (points.Count < count) return null;

        // Pick distinct waypoints, then fly them in the order of their bearing so the tour is a loop, not a zigzag.
        var chosen = points.OrderBy(_ => rng.Next()).Take(count)
            .OrderBy(x => origin.Position.InitialCourseTo(x.Airport.Position))
            .Select(x => new TourWaypoint(Label(x.Airport), x.Airport.Position, tour.MinAglFt, tour.MaxAglFt))
            .ToList();
        double length = TourWaypoint.TourLengthNm(origin.Position, chosen);
        if (length > range * 0.8) return null;

        var offer = StandardMissionGenerator.BuildLeg(ctx, origin, origin, length, now, ctx.Search);
        return offer is null ? null : offer with { Waypoints = chosen };
    }

    /// <summary>What a waypoint is called: the town an airfield serves, else the airfield's name.</summary>
    public static string Label(Airport a) => a.City is { Length: > 0 } city ? city : a.Name;
}
