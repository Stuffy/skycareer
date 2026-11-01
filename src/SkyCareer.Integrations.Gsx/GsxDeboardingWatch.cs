namespace SkyCareer.Integrations.Gsx;

/// <summary>
/// Follows GSX deboarding for one flight. GSX reports <see cref="GsxServiceState.Completed"/> only briefly and then drops
/// the state back to <see cref="GsxServiceState.Callable"/> (seen 2026-11-01: the settlement waited for the 20 min
/// timeout), so a deboarding that was seen running counts as completed once it no longer runs.
/// </summary>
public sealed class GsxDeboardingWatch
{
    public bool Seen { get; private set; }

    /// <summary>Requested/Performing while it runs, Completed once it ran, otherwise the state GSX reports.</summary>
    public GsxServiceState Observe(GsxState s)
    {
        if (!s.Detected) return s.Deboarding;
        if (s.Deboarding is GsxServiceState.Requested or GsxServiceState.Performing or GsxServiceState.Completed) Seen = true;
        return s.Deboarding is GsxServiceState.Requested or GsxServiceState.Performing ? s.Deboarding
            : Seen ? GsxServiceState.Completed
            : s.Deboarding;
    }
}
