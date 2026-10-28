namespace SkyCareer.Domain.Loading;

public sealed record LoadDeviation(string Item, Mass Expected, Mass Actual)
{
    public Mass Difference => Actual - Expected;
}

/// <summary>
/// Compares what the simulator reports after loading with the load sheet. A flight may only
/// block out once payload and fuel match within tolerance.
/// </summary>
public static class LoadVerifier
{
    public const double RelativeTolerance = 0.02;
    public const double AbsoluteToleranceKg = 5;

    /// <summary>Allowed deviation from an expected mass: 2 %, at least 5 kg.</summary>
    public static double ToleranceKg(Mass expected) => Math.Max(expected.Kg * RelativeTolerance, AbsoluteToleranceKg);

    /// <param name="relativeTolerance">Allowed relative deviation (default 2 %).</param>
    /// <param name="absoluteToleranceKg">Floor so tiny loads (e.g. an empty station) do not fail on rounding.</param>
    public static IReadOnlyList<LoadDeviation> Verify(LoadSheet sheet, Mass actualPayload, Mass actualFuel,
        double relativeTolerance = RelativeTolerance, double absoluteToleranceKg = AbsoluteToleranceKg)
    {
        var deviations = new List<LoadDeviation>();
        Check("Payload", sheet.SimPayload, actualPayload);
        Check("Fuel", sheet.Fuel.Block, actualFuel);
        return deviations;

        void Check(string item, Mass expected, Mass actual)
        {
            double allowed = Math.Max(expected.Kg * relativeTolerance, absoluteToleranceKg);
            if (Math.Abs(actual.Kg - expected.Kg) > allowed)
                deviations.Add(new LoadDeviation(item, expected, actual));
        }
    }
}
