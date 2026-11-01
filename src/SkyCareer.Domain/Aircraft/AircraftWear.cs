using System.Globalization;
using SkyCareer.Localization;

namespace SkyCareer.Domain.Aircraft;

/// <summary>
/// One MSFS 2024 wear-and-tear component (SimVar <c>WEAR AND TEAR LEVEL:{Id}</c>). <see cref="GroupKey"/> is the stable
/// group id (one of the <see cref="WearComponents"/> constants); <see cref="Name"/> and <see cref="Group"/> are display texts.
/// </summary>
public sealed record WearComponent(int Id, string GroupKey)
{
    /// <summary>Localized component name.</summary>
    public string Name => WearComponents.ComponentName(Id);

    /// <summary>Localized group name.</summary>
    public string Group => WearComponents.GroupName(GroupKey);
}

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

    /// <summary>Stable group ids (not shown as such; see <see cref="GroupName"/>).</summary>
    public const string LandingGear = "Landing gear", Tires = "Tires", Brakes = "Brakes", Engine = "Engine",
        Controls = "Flight controls", Hydraulics = "Hydraulics", Electrics = "Electrical", Fuel = "Fuel system",
        Autopilot = "Autopilot", General = "General";

    public static IReadOnlyList<WearComponent> All { get; } =
    [
        new(0, Fuel), new(1, Fuel), new(2, Fuel), new(3, Fuel), new(4, Fuel),
        new(5, Controls), new(6, Controls), new(7, Controls), new(8, Controls), new(9, Controls), new(10, Controls),
        new(11, Controls), new(12, Controls), new(13, Controls), new(14, Controls), new(15, Controls), new(16, Controls),
        new(17, Controls), new(18, Controls), new(19, Controls), new(20, Controls),
        new(21, Hydraulics), new(22, Hydraulics), new(23, Hydraulics), new(24, Hydraulics), new(25, Hydraulics), new(26, Hydraulics),
        new(27, Hydraulics),
        new(28, Electrics), new(29, Electrics), new(30, Electrics), new(31, Electrics), new(32, Electrics), new(33, Electrics), new(34, Electrics),
        new(35, LandingGear), new(36, Brakes), new(37, Tires), new(38, Tires),
        new(39, Engine), new(40, Engine), new(41, Engine), new(42, Engine),
        new(43, Autopilot), new(44, Autopilot), new(45, Autopilot),
        new(46, General),
        new(47, LandingGear), new(48, Tires), new(49, Tires)
    ];

    /// <summary>Localized name of a group id.</summary>
    public static string GroupName(string group) => group switch
    {
        LandingGear => L.T("Landing gear"),
        Tires => L.T("Tires"),
        Brakes => L.T("Brakes"),
        Engine => L.T("Engine"),
        Controls => L.T("Flight controls"),
        Hydraulics => L.T("Hydraulics"),
        Electrics => L.T("Electrical"),
        Fuel => L.T("Fuel system"),
        Autopilot => L.T("Autopilot"),
        General => L.T("General"),
        _ => group
    };

    /// <summary>Localized name of a component id.</summary>
    public static string ComponentName(int id) => id switch
    {
        0 => L.T("Fuel tank"), 1 => L.T("Fuel pump"), 2 => L.T("Fuel valve"), 3 => L.T("Fuel manifold"), 4 => L.T("Fuel line"),
        5 => L.T("Left flaps"), 6 => L.T("Right flaps"), 7 => L.T("Left flap cable"), 8 => L.T("Right flap cable"),
        9 => L.T("Left aileron"), 10 => L.T("Right aileron"), 11 => L.T("Left aileron cable"), 12 => L.T("Right aileron cable"),
        13 => L.T("Elevator"), 14 => L.T("Elevator cable"), 15 => L.T("Left elevator"), 16 => L.T("Right elevator"),
        17 => L.T("Left elevator cable"), 18 => L.T("Right elevator cable"), 19 => L.T("Rudder"), 20 => L.T("Rudder cable"),
        21 => L.T("Hydraulic reservoir"), 22 => L.T("Hydraulic pump"), 23 => L.T("Hydraulic valve"), 24 => L.T("Hydraulic actuator"),
        25 => L.T("Hydraulic PTU"), 26 => L.T("Hydraulic accumulator"), 27 => L.T("Hydraulic line"),
        28 => L.T("Alternator"), 29 => L.T("Battery"), 30 => L.T("IDG"), 31 => L.T("RAT"), 32 => L.T("Electrical bus"),
        33 => L.T("Circuit"), 34 => L.T("Rectifier"),
        35 => L.T("Landing gear"), 36 => L.T("Brakes"), 37 => L.T("Tires"), 38 => L.T("Tire pressure"),
        39 => L.T("Piston engine"), 40 => L.T("Turbine"), 41 => L.T("Oil tank"), 42 => L.T("Oil quantity"),
        43 => L.T("Autopilot roll axis"), 44 => L.T("Autopilot pitch axis"), 45 => L.T("Autopilot yaw axis"),
        46 => L.T("General"),
        47 => L.T("Tailwheel gear"), 48 => L.T("Tailwheel tire"), 49 => L.T("Tailwheel tire pressure"),
        _ => id.ToString(CultureInfo.InvariantCulture)
    };

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
        Find(id) is { } c ? RepairShare(c.GroupKey) / All.Count(x => x.GroupKey == c.GroupKey) : 0;

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

    /// <summary>The worst component per group (localized group name), worst group first.</summary>
    public IReadOnlyList<(string Group, double Level)> Groups() =>
        Levels.GroupBy(kv => WearComponents.All[kv.Key].GroupKey)
              .Select(g => (WearComponents.GroupName(g.Key), g.Min(kv => kv.Value)))
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

    /// <summary>For a <see cref="Delta"/>: the largest loss per group (localized group name), largest first.</summary>
    public IReadOnlyList<(string Group, double Loss)> GroupLosses() =>
        Levels.GroupBy(kv => WearComponents.All[kv.Key].GroupKey)
              .Select(g => (WearComponents.GroupName(g.Key), g.Max(kv => kv.Value)))
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
