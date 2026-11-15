using SkyCareer.Domain;
using SkyCareer.Domain.Career;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Settlement;

/// <summary>How the passengers experienced the flight, 0–100 overall and per aspect.</summary>
public sealed record ComfortRating(int Score, int GLoad, int Bank, int Descent, int Landing)
{
    /// <summary>The flight without the landing; this is the score's "smoothness" on passenger flights.</summary>
    public int InFlight => (int)Math.Round(
        (GLoad * PassengerComfort.GWeight + Bank * PassengerComfort.BankWeight + Descent * PassengerComfort.DescentWeight)
        / (PassengerComfort.GWeight + PassengerComfort.BankWeight + PassengerComfort.DescentWeight));
}

/// <summary>
/// Rates a passenger flight from how it was flown (<see cref="ComfortStats"/>) and landed, and words a few passenger
/// comments about it. Pure functions; the comments are picked by a seed so the same flight always gets the same ones.
/// </summary>
public static class PassengerComfort
{
    public const double GWeight = 0.30, BankWeight = 0.25, DescentWeight = 0.20, LandingWeight = 0.25;
    /// <summary>Sustained bank up to this is comfortable; the bank score reaches 0 at twice the angle.</summary>
    public const double ComfortBankDeg = 25;
    /// <summary>Sustained sink rate up to this is comfortable; the descent score reaches 0 at twice the rate.</summary>
    public const double ComfortSinkFpm = 2500;
    /// <summary>An aspect at or above this is praised, below <see cref="Poor"/> complained about.</summary>
    public const int Good = 90, Poor = 60;

    public static ComfortRating Rate(FlightResult r, ScoringRule rule, ComfortStats stats)
    {
        double gLimit = Math.Min(rule.MaxGForce, 1.5);
        double g = 100 - Math.Max(0, stats.SustainedG - gLimit) * 200 - Math.Min(40, stats.GExcursionSeconds * 2);
        double bank = 100 - Math.Max(0, stats.SustainedBankDeg - ComfortBankDeg) * (100 / ComfortBankDeg) - Math.Min(30, stats.SteepBankSeconds * 0.5);
        double descent = 100 - Math.Max(0, stats.SustainedSinkFpm - ComfortSinkFpm) * (100 / ComfortSinkFpm) - Math.Min(30, stats.HighSinkSeconds * 0.5);
        double landing = FlightScorer.Landing(rule, r);
        g = Math.Clamp(g, 0, 100);
        bank = Math.Clamp(bank, 0, 100);
        descent = Math.Clamp(descent, 0, 100);
        double total = g * GWeight + bank * BankWeight + descent * DescentWeight + landing * LandingWeight;
        return new ComfortRating((int)Math.Round(total), (int)Math.Round(g), (int)Math.Round(bank), (int)Math.Round(descent), (int)Math.Round(landing));
    }

    /// <summary>
    /// Up to three passenger comments: complaints about the worst aspects first, then lateness, then praise and the GSX
    /// services they noticed. <paramref name="seed"/> (the job's id) picks the wording.
    /// </summary>
    public static IReadOnlyList<string> Comments(ComfortRating rating, TimeSpan lateBy, IReadOnlyList<GroundService> services, object seed)
    {
        var comments = new List<string>();
        string Pick(string key, params string[] variants) => variants[StableHash.Of(seed, key) % variants.Length];

        var aspects = new (string Key, int Value)[] { ("landing", rating.Landing), ("g", rating.GLoad), ("bank", rating.Bank), ("descent", rating.Descent) };
        foreach (var (key, _) in aspects.Where(a => a.Value < Poor).OrderBy(a => a.Value))
            comments.Add(key switch
            {
                "landing" => Pick(key, L.T("That landing rattled my teeth."), L.T("Did we land, or were we shot down?")),
                "g" => Pick(key, L.T("My stomach is still somewhere over the last waypoint."), L.T("It felt like a rollercoaster up there.")),
                "bank" => Pick(key, L.T("Those turns were far too steep for my taste."), L.T("I could look straight down from my window – no thanks.")),
                _ => Pick(key, L.T("My ears are still ringing from that descent."), L.T("We dropped like a stone."))
            });

        if (lateBy > TimeSpan.FromMinutes(15))
            comments.Add(Pick("late", L.T("We arrived late – I missed my connection."), L.T("Late again. Not impressed.")));

        if (rating.Landing >= Good)
            comments.Add(Pick("landing+", L.T("I didn't even notice we had landed."), L.T("The smoothest landing I've had in years.")));
        if (rating.GLoad >= Good && rating.Bank >= Good)
            comments.Add(Pick("flight+", L.T("A calm, steady flight – I slept the whole way."), L.T("My coffee never moved.")));
        if (services.Contains(GroundService.Catering))
            comments.Add(L.T("The catering was a nice touch."));
        else if (services.Contains(GroundService.Cleaning))
            comments.Add(L.T("The cabin was spotless."));
        if (lateBy <= TimeSpan.Zero && rating.Score >= Good)
            comments.Add(L.T("Right on time, as promised."));
        if (rating.Descent >= Good && rating.Score >= Good)
            comments.Add(L.T("A pleasant descent – my ears say thank you."));

        if (comments.Count == 0) comments.Add(L.T("An ordinary flight, nothing to complain about."));
        return comments.Take(3).ToList();
    }
}
