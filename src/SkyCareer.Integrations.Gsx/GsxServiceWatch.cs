using SkyCareer.Domain.Career;

namespace SkyCareer.Integrations.Gsx;

/// <summary>
/// Follows the optional GSX services (catering, cleaning, water, lavatory) while a job is prepared at the stand. A service
/// counts only when it was seen requested or running for this job: GSX can still show <see cref="GsxServiceState.Completed"/>
/// from an earlier flight at sim start, and like deboarding it may drop back to <see cref="GsxServiceState.Callable"/>
/// right after finishing, so a service seen performing counts once it no longer runs.
/// </summary>
public sealed class GsxServiceWatch
{
    private readonly Dictionary<GroundService, (bool Started, bool Performed)> _seen = [];
    private readonly List<GroundService> _completed = [];

    public IReadOnlyList<GroundService> Completed => _completed;

    public void Observe(GsxState s)
    {
        if (!s.Detected) return;
        Track(GroundService.Catering, s.Catering);
        Track(GroundService.Cleaning, s.Cleaning);
        Track(GroundService.Water, s.Water);
        Track(GroundService.Lavatory, s.Lavatory);
    }

    private void Track(GroundService service, GsxServiceState state)
    {
        if (_completed.Contains(service)) return;
        var (started, performed) = _seen.GetValueOrDefault(service);
        bool running = state is GsxServiceState.Requested or GsxServiceState.Performing;
        if (started && (state == GsxServiceState.Completed || performed && !running && state != GsxServiceState.Unknown))
        {
            _completed.Add(service);
            return;
        }
        _seen[service] = (started || running, performed || state == GsxServiceState.Performing);
    }
}
