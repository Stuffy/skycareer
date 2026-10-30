using System.Globalization;

namespace SkyCareer.Domain.Aircraft;

/// <summary>One MSFS 2024 wear-and-tear component (SimVar <c>WEAR AND TEAR LEVEL:{Id}</c>).</summary>
public sealed record WearComponent(int Id, string Name, string Group);

/// <summary>
/// The wear-and-tear components of MSFS 2024 (ids from the SDK's <c>Helpers.xml</c>) and how SkyCareer uses them.
/// The sim reports each as remaining condition (1 = new); components an aircraft doesn't have read 0.
/// </summary>
public static class WearComponents
{
    /// <summary>Below this a component shows as a warning (departure check, fleet).</summary>
    public const double WarningLevel = 0.70;

    /// <summary>
    /// Condition points per percentage point of average component loss reported by the sim during a flight. A first
    /// estimate, to be tuned once real per-flight deltas are known.
    /// </summary>
    public const double SimWearFactor = 1.0;

    public const string LandingGear = "Fahrwerk", Tires = "Reifen", Brakes = "Bremsen", Engine = "Triebwerk",
        Controls = "Steuerflächen", Hydraulics = "Hydraulik", Electrics = "Elektrik", Fuel = "Kraftstoffsystem",
        Autopilot = "Autopilot", General = "Allgemein";

    public static IReadOnlyList<WearComponent> All { get; } =
    [
        new(0, "Kraftstofftank", Fuel), new(1, "Kraftstoffpumpe", Fuel), new(2, "Kraftstoffventil", Fuel),
        new(3, "Kraftstoffverteiler", Fuel), new(4, "Kraftstoffleitung", Fuel),
        new(5, "Klappen links", Controls), new(6, "Klappen rechts", Controls), new(7, "Klappenseil links", Controls),
        new(8, "Klappenseil rechts", Controls), new(9, "Querruder links", Controls), new(10, "Querruder rechts", Controls),
        new(11, "Querruderseil links", Controls), new(12, "Querruderseil rechts", Controls), new(13, "Höhenruder", Controls),
        new(14, "Höhenruderseil", Controls), new(15, "Höhenruder links", Controls), new(16, "Höhenruder rechts", Controls),
        new(17, "Höhenruderseil links", Controls), new(18, "Höhenruderseil rechts", Controls), new(19, "Seitenruder", Controls),
        new(20, "Seitenruderseil", Controls),
        new(21, "Hydraulikbehälter", Hydraulics), new(22, "Hydraulikpumpe", Hydraulics), new(23, "Hydraulikventil", Hydraulics),
        new(24, "Hydraulikaktuator", Hydraulics), new(25, "Hydraulik-PTU", Hydraulics), new(26, "Hydraulikspeicher", Hydraulics),
        new(27, "Hydraulikleitung", Hydraulics),
        new(28, "Generator", Electrics), new(29, "Batterie", Electrics), new(30, "IDG", Electrics), new(31, "RAT", Electrics),
        new(32, "Stromschiene", Electrics), new(33, "Stromkreis", Electrics), new(34, "Gleichrichter", Electrics),
        new(35, "Fahrwerk", LandingGear), new(36, "Bremsen", Brakes), new(37, "Reifen", Tires), new(38, "Reifendruck", Tires),
        new(39, "Kolbentriebwerk", Engine), new(40, "Turbine", Engine), new(41, "Öltank", Engine), new(42, "Ölmenge", Engine),
        new(43, "Autopilot Querachse", Autopilot), new(44, "Autopilot Längsachse", Autopilot), new(45, "Autopilot Gierachse", Autopilot),
        new(46, "Allgemein", General),
        new(47, "Spornradfahrwerk", LandingGear), new(48, "Spornradreifen", Tires), new(49, "Spornradreifendruck", Tires)
    ];

    /// <summary>Share of the new price that replacing a fully worn group costs (tires are cheap, engines are not).</summary>
    public static double RepairShare(string group) => group switch
    {
        Tires => 0.002,
        Brakes => 0.003,
        LandingGear => 0.01,
        Engine => 0.05,
        Controls => 0.01,
        General => 0.01,
        _ => 0.005
    };

    /// <summary>
    /// Share of the new price that replacing one fully worn component costs: the group's share split evenly over its
    /// components, so single, group and full repairs add up to the same amount.
    /// </summary>
    public static double ComponentRepairShare(int id) =>
        Find(id) is { } c ? RepairShare(c.Group) / All.Count(x => x.Group == c.Group) : 0;

    /// <summary>How long the aircraft is in the workshop for a repair in this group.</summary>
    public static TimeSpan RepairDowntime(string group) => group switch
    {
        Tires or Brakes => TimeSpan.FromHours(2),
        Engine => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(6)
    };

    public static string SimVar(int id) => $"WEAR AND TEAR LEVEL:{id}";

    public static WearComponent? Find(int id) => id >= 0 && id < All.Count ? All[id] : null;
}

/// <summary>
/// Condition per wear component (0–1, 1 = new), keyed by <see cref="WearComponent.Id"/>. Used both for sim readings
/// (only components the aircraft has, i.e. &gt; 0) and for SkyCareer's own state per aircraft (missing = 1, as new).
/// Stored as text "35=0.9775;37=0.98".
/// </summary>
public sealed record AircraftWear(IReadOnlyDictionary<int, double> Levels)
{
    public static AircraftWear Empty { get; } = new(new Dictionary<int, double>());

    /// <summary>A sim reading: values ≤ 0 mean the aircraft doesn't have the component and are dropped.</summary>
    public static AircraftWear FromSim(IReadOnlyDictionary<int, double> raw) =>
        new(raw.Where(kv => kv.Value > 0 && WearComponents.Find(kv.Key) is not null)
               .ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value, 0, 1)));

    public bool IsEmpty => Levels.Count == 0;

    /// <summary>Condition of a component; components without a value count as new.</summary>
    public double Level(int id) => Levels.TryGetValue(id, out var v) ? v : 1;

    /// <summary>The worst component, or null when nothing is worn.</summary>
    public (WearComponent Component, double Level)? Worst =>
        Levels.Count == 0 ? null : Levels.OrderBy(kv => kv.Value).Select(kv => (WearComponents.All[kv.Key], kv.Value)).First();

    public double WorstLevel => Levels.Count == 0 ? 1 : Math.Min(1, Levels.Values.Min());

    /// <summary>The worst component per group, worst group first.</summary>
    public IReadOnlyList<(string Group, double Level)> Groups() =>
        Levels.GroupBy(kv => WearComponents.All[kv.Key].Group)
              .Select(g => (g.Key, g.Min(kv => kv.Value)))
              .OrderBy(g => g.Item2)
              .ToList();

    /// <summary>Groups below <see cref="WearComponents.WarningLevel"/>.</summary>
    public IReadOnlyList<(string Group, double Level)> Warnings() =>
        Groups().Where(g => g.Level < WearComponents.WarningLevel).ToList();

    /// <summary>
    /// What the sim wore off between two readings: max(0, start − end) per component present in both (0 included, so
    /// the delta also says which components the aircraft has). Components that got better (repaired in the sim) count
    /// as no wear.
    /// </summary>
    public static AircraftWear Delta(AircraftWear start, AircraftWear end) =>
        new(start.Levels.Where(kv => end.Levels.ContainsKey(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - end.Levels[kv.Key])));

    /// <summary>For a <see cref="Delta"/>: the largest loss per group, largest first.</summary>
    public IReadOnlyList<(string Group, double Loss)> GroupLosses() =>
        Levels.GroupBy(kv => WearComponents.All[kv.Key].Group)
              .Select(g => (g.Key, g.Max(kv => kv.Value)))
              .OrderByDescending(g => g.Item2)
              .ToList();

    /// <summary>For a <see cref="Delta"/>: the average loss over the aircraft's components (0–1).</summary>
    public double AverageLoss => Levels.Count == 0 ? 0 : Levels.Values.Average();

    /// <summary>Applies a <see cref="Delta"/> (losses) to an own state.</summary>
    public AircraftWear Wear(AircraftWear delta)
    {
        var levels = Levels.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (id, loss) in delta.Levels) levels[id] = Math.Max(0, Level(id) - loss);
        return new(levels);
    }

    /// <summary>Sets the given components back to new; the others stay as they are.</summary>
    public AircraftWear RepairComponents(IEnumerable<int> ids)
    {
        var levels = Levels.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (int id in ids)
            if (levels.ContainsKey(id)) levels[id] = 1;
        return new(levels);
    }

    /// <summary>Raises every component by <paramref name="amount"/> (capped at new); 1 restores everything.</summary>
    public AircraftWear Restore(double amount) =>
        new(Levels.ToDictionary(kv => kv.Key, kv => Math.Min(1, kv.Value + amount)));

    public string Serialize() =>
        string.Join(';', Levels.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.ToString("0.#####", CultureInfo.InvariantCulture)}"));

    public static AircraftWear Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        var levels = new Dictionary<int, double>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                WearComponents.Find(id) is not null && double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                levels[id] = v;
        }
        return new(levels);
    }
}
