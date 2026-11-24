using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Localization;
using SkyCareer.Missions.Events;
using SkyCareer.Sim;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Application;

/// <summary>An event waiting for the crew's decision: where to go instead and what it pays.</summary>
public sealed record PendingInFlightEvent(Guid ContractId, InFlightEventKind Kind, string Title, string Message, Airport? Alternate,
    decimal? NewPayout, double ExtraHours, DateTime DecideByUtc, string AcceptText, string DeclineText);

public sealed partial class FlightAutomation
{
    /// <summary>Whether events during the flight are on (app setting); null = on.</summary>
    public Func<bool>? EventsEnabled { get; set; }

    /// <summary>The event waiting for a decision; null when none.</summary>
    public PendingInFlightEvent? PendingEvent { get; private set; }

    /// <summary>Raised when an event needs a decision (e.g. to play a sound).</summary>
    public event Action<PendingInFlightEvent>? EventRaised;

    /// <summary>Counts decisions, so pages showing the destination know to load it again.</summary>
    public int EventVersion { get; private set; }

    private readonly Dictionary<Guid, PlannedEvent?> _planned = [];
    private readonly HashSet<Guid> _eventDone = [];
    private volatile int _decision; // 0 none, 1 accept, 2 decline

    /// <summary>The crew's answer to <see cref="PendingEvent"/>; applied on the next tick.</summary>
    public void DecideEvent(bool accept) => _decision = accept ? 1 : 2;

    /// <summary>Debug: raises an event for the flight in progress on the next tick.</summary>
    public void TriggerEventNow(InFlightEventKind kind) => _forced = kind;
    private InFlightEventKind? _forced;

    private async Task FollowEventsAsync(MissionContract job, FlightPhase? phase, Telemetry t)
    {
        if (PendingEvent is { } pending && pending.ContractId == job.Id)
        {
            bool timedOut = DateTime.UtcNow >= pending.DecideByUtc;
            if (_decision == 0 && !timedOut) return;
            bool accept = _decision == 1 && !timedOut;
            _decision = 0;
            PendingEvent = null;
            await career.RecordEventAsync(job.Id, pending.Kind, accept, accept ? pending.Alternate?.Ident : null, accept ? pending.NewPayout : null,
                accept ? pending.ExtraHours : 0);
            host.Log(accept
                ? L.F("{0}: now flying to {1}", pending.Title, pending.Alternate?.Ident ?? "")
                : L.F("{0}: continuing to {1}", pending.Title, job.EffectiveDestination));
            EventVersion++;
            _eventDone.Add(job.Id);
            Invalidate();
            return;
        }
        if (_eventDone.Contains(job.Id) || job.EventKind is not null || phase is not (FlightPhase.Climb or FlightPhase.Cruise or FlightPhase.Descent)) return;

        PlannedEvent? plan;
        if (_forced is { } forced)
        {
            _forced = null;
            plan = new PlannedEvent(forced, 0);
        }
        else
        {
            if (EventsEnabled?.Invoke() == false) return;
            if (!_planned.TryGetValue(job.Id, out plan))
            {
                plan = _fleet.TryGetValue(job.AircraftId, out var plane) ? InFlightEvents.Plan(job, career.MissionType(job.MissionTypeId), plane) : null;
                _planned[job.Id] = plan;
            }
            if (plan is null || host.Summarize()?.TakeoffTime is not { } takeoff) return;
            var profile0 = career.Profile(_fleet[job.AircraftId].ProfileId);
            double plannedHours = job.DistanceNm / Math.Max(1, profile0.CruiseKtas);
            if ((t.Timestamp - takeoff).TotalHours < plannedHours * plan.TriggerFraction) return;
        }

        if (await BuildEventAsync(job, plan.Kind, t) is { } raised)
        {
            PendingEvent = raised;
            host.Log(raised.Title);
            EventRaised?.Invoke(raised);
        }
        else _eventDone.Add(job.Id); // nothing sensible to offer (e.g. the destination is the nearest airfield)
    }

    private async Task<PendingInFlightEvent?> BuildEventAsync(MissionContract job, InFlightEventKind kind, Telemetry t)
    {
        if (!_fleet.TryGetValue(job.AircraftId, out var plane)) return null;
        var profile = career.Profile(plane.ProfileId);
        var destination = (await career.GetAirportsAsync([job.EffectiveDestination])).GetValueOrDefault(job.EffectiveDestination);
        var origin = (await career.GetAirportsAsync([job.Origin])).GetValueOrDefault(job.Origin);
        if (destination is null || origin is null) return null;
        double toDestination = t.Position.DistanceNmTo(destination.Position);
        var decideBy = DateTime.UtcNow + InFlightEvents.DecisionTime;
        string title = InFlightEvents.Title(kind);

        switch (kind)
        {
            case InFlightEventKind.MedicalEmergency:
            case InFlightEventKind.TechnicalWarning:
            {
                var around = await career.AirportsAroundAsync(t.Position, Math.Max(30, toDestination));
                var alternate = InFlightEvents.NearestSuitable(t.Position, around, profile, except: destination.Ident);
                if (alternate is null || alternate.Position.DistanceNmTo(t.Position) >= toDestination) return null;
                var nav = Navigation.DirectTo(t.Position, alternate.Position, t.GroundSpeedKt, t.MagVar);
                string where = L.F("{0} ({1}) {2:N0} nm, {3}", alternate.Ident, alternate.Name, nav.DistanceNm, nav.CourseText);
                if (kind == InFlightEventKind.MedicalEmergency)
                    return new PendingInFlightEvent(job.Id, kind, title,
                        L.F("A passenger needs urgent medical help. Nearest suitable airfield: {0}. Your destination {1} is still {2:N0} nm away.", where, destination.Ident, toDestination),
                        alternate, null, 0, decideBy, L.T("Divert"), L.T("Continue to destination"));
                var worst = plane.Components.Worst;
                decimal pay = InFlightEvents.PrecautionaryPayout(job, origin, alternate);
                return new PendingInFlightEvent(job.Id, kind, title,
                    L.F("Warning light: {0} at {1:P0}. Precautionary landing at {2}? The customer pays the share flown ({3}); flying on wears the aircraft more.",
                        worst is { } w ? w.Component.Name : L.T("a component"), worst?.Level ?? 0, where, L.Eur(pay)),
                    alternate, pay, 0, decideBy, L.T("Land as a precaution"), L.T("Continue to destination"));
            }
            default:
            {
                var around = await career.AirportsAroundAsync(destination.Position, InFlightEvents.ChangeMaxNm);
                var changed = InFlightEvents.NewDestination(job, destination, around, profile);
                if (changed is null) return null;
                decimal pay = InFlightEvents.ChangedPayout(job, origin, changed);
                double extra = Math.Max(0, (origin.Position.DistanceNmTo(changed.Position) - job.DistanceNm) / Math.Max(1, profile.CruiseKtas)) + 0.25;
                return new PendingInFlightEvent(job.Id, kind, title,
                    L.F("Your passengers now want to go to {0} ({1}) instead of {2}. The new destination pays {3} instead of {4}.",
                        changed.Ident, changed.Name, destination.Ident, L.Eur(pay), L.Eur(job.Payout)),
                    changed, pay, extra, decideBy, L.T("Accept the new destination"), L.T("Decline"));
            }
        }
    }
}
