namespace SkyCareer.Domain.Career;

/// <summary>
/// How the aircraft was flown between liftoff and touchdown, for the passenger comfort rating and the handling limits of
/// sensitive loads. The "sustained" values are the highest level held for a moment (bank 3 s, sink 5 s, G 1 s), so a
/// single spike in the data doesn't count. Bank is 0 when the recording has no attitude data (flights before 0.5).
/// </summary>
public sealed record ComfortStats
{
    /// <summary>Bank beyond this (degrees) counts into <see cref="SteepBankSeconds"/>.</summary>
    public const double SteepBankDeg = 30;
    /// <summary>Sink rate beyond this (ft/min) counts into <see cref="HighSinkSeconds"/>.</summary>
    public const double HighSinkFpm = 3000;
    /// <summary>G outside this band counts into <see cref="GExcursionSeconds"/>.</summary>
    public const double MinComfortG = 0.6, MaxComfortG = 1.4;

    public double AirborneSeconds { get; init; }
    public double MaxBankDeg { get; init; }
    public double SustainedBankDeg { get; init; }
    public double SteepBankSeconds { get; init; }
    /// <summary>Highest sink rate, ft/min as a positive number.</summary>
    public double MaxSinkFpm { get; init; }
    public double SustainedSinkFpm { get; init; }
    public double HighSinkSeconds { get; init; }
    public double MaxClimbFpm { get; init; }
    public double SustainedG { get; init; } = 1;
    public double GExcursionSeconds { get; init; }
}

/// <summary>What the landing was flown in, taken at touchdown; values the recording doesn't have stay null.</summary>
/// <param name="Night">Sun more than 6° below the horizon (after civil twilight).</param>
/// <param name="CrosswindKt">Wind component across the aircraft's heading at touchdown.</param>
public sealed record LandingConditions(bool? Night, double? VisibilityM, double? CrosswindKt, double? WindSpeedKt = null, double? WindDirectionDeg = null);
