using SkyCareer.Domain.Career;

namespace SkyCareer.Missions.Settlement;

public sealed record FlightScore(int Total, int Landing, int Smoothness, int Punctuality, int SafetyPenalty);

/// <summary>Scores a flight 0–100 using the mission type's weights, so e.g. VIP and ambulance flights punish rough flying harder.</summary>
public static class FlightScorer
{
    public static FlightScore Score(ScoringRule rule, FlightResult r, TimeSpan lateBy)
    {
        double maxFpm = Math.Max(50, rule.MaxTouchdownFpm);
        double fpm = Math.Abs(r.TouchdownFpm);
        double landing = fpm <= maxFpm * 0.5 ? 100 : 100 * (1 - (fpm - maxFpm * 0.5) / (maxFpm * 1.5));
        landing = Math.Clamp(landing - r.Bounces * 10, 0, 100);

        double smooth = r.MaxGForce <= rule.MaxGForce ? 100 : 100 - (r.MaxGForce - rule.MaxGForce) * 200;
        smooth = Math.Clamp(smooth, 0, 100);

        double punctuality = lateBy <= TimeSpan.Zero ? 100 : Math.Clamp(100 - lateBy.TotalMinutes / 15 * 10, 0, 100);

        double weights = rule.LandingRateWeight + rule.GForceWeight + rule.PunctualityWeight;
        double weighted = weights <= 0 ? 100
            : (landing * rule.LandingRateWeight + smooth * rule.GForceWeight + punctuality * rule.PunctualityWeight) / weights;

        int safety = (int)Math.Min(40, (r.OverspeedSeconds + r.StallWarningSeconds) * 2 + r.MinorViolations * 10);
        int total = (int)Math.Round(Math.Clamp(weighted - safety, 0, 100));
        return new FlightScore(total, (int)Math.Round(landing), (int)Math.Round(smooth), (int)Math.Round(punctuality), safety);
    }
}
