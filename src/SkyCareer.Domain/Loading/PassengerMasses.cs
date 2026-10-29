namespace SkyCareer.Domain.Loading;

/// <summary>Whether passengers check a bag. All checked-bag profiles count one bag per passenger, as SimBrief does.</summary>
public enum BaggageProfile { None, HandOnly, Domestic, European, Intercontinental, Leisure }

public enum PassengerMassMode
{
    /// <summary>EASA standard masses (hand baggage included). Typical for airline operations.</summary>
    Standard,
    /// <summary>Individually drawn masses around the standard mean. More variety for small aircraft.</summary>
    Randomized
}

public sealed record PassengerCount(int Adults, int Children = 0, int Infants = 0)
{
    /// <summary>Infants sit on laps and do not occupy a seat.</summary>
    public int Seated => Adults + Children;
    public int Total => Adults + Children + Infants;
}

/// <summary>
/// Passenger and baggage mass tables. The passenger weight follows SimBrief (175 lb), which weighs every passenger the
/// same; bags (50 lb on average) vary per passenger and reach SimBrief as freight. A career sets its own values
/// (<see cref="Career.Career.PassengerWeightKg"/>, <see cref="Career.Career.BagWeightKg"/>) via <see cref="ForCareer"/>.
/// </summary>
public sealed record PassengerMassTable
{
    /// <summary>SimBrief's default passenger weight, 175 lb.</summary>
    public const double SimBriefAdultKg = 79.38;
    /// <summary>Average checked bag, 50 lb (SimBrief's usual airframe value).</summary>
    public const double SimBriefBagKg = 22.68;

    public double AdultKg { get; init; } = SimBriefAdultKg;
    public double ChildKg { get; init; } = 35;
    public double InfantKg { get; init; } = 10;
    public double AdultStdDevKg { get; init; } = 14;
    public double BagKg { get; init; } = SimBriefBagKg;

    public double BaggageKgPerPax(BaggageProfile profile) =>
        profile is BaggageProfile.None or BaggageProfile.HandOnly ? 0 : BagKg;

    public static PassengerMassTable Default { get; } = new();

    /// <summary>A career's masses. SimBrief knows no children, so they weigh as much as adults.</summary>
    public static PassengerMassTable ForCareer(double passengerKg, double bagKg) =>
        new() { AdultKg = passengerKg, ChildKg = passengerKg, BagKg = bagKg };

    public Mass PassengerMass(PassengerCount pax, PassengerMassMode mode, Random rng)
    {
        double children = pax.Children * ChildKg + pax.Infants * InfantKg;
        if (mode == PassengerMassMode.Standard)
            return Mass.FromKg(pax.Adults * AdultKg + children);

        double adults = 0;
        for (int i = 0; i < pax.Adults; i++)
            adults += Math.Clamp(NextGaussian(rng, AdultKg, AdultStdDevKg), 45, 160);
        return Mass.FromKg(adults + children);
    }

    /// <summary>
    /// Checked baggage. Randomized mode varies each bag ±40 % and lets 15 % of passengers travel without one; the
    /// expected total stays at one <see cref="BagKg"/> bag per seated passenger.
    /// </summary>
    public Mass BaggageMass(PassengerCount pax, BaggageProfile profile, PassengerMassMode mode, Random rng)
    {
        double perPax = BaggageKgPerPax(profile);
        int bagCarriers = pax.Adults + pax.Children;
        if (mode == PassengerMassMode.Standard || perPax == 0)
            return Mass.FromKg(bagCarriers * perPax);

        double total = 0;
        for (int i = 0; i < bagCarriers; i++)
        {
            if (rng.NextDouble() < 0.15) continue;
            total += perPax * (0.6 + rng.NextDouble() * 0.8) / 0.85;
        }
        return Mass.FromKg(total);
    }

    private static double NextGaussian(Random rng, double mean, double stdDev)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
