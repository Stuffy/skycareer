using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Loading;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Integrations.Gsx;

/// <summary>What the app should write to the sim next to stay in step with GSX.</summary>
public sealed record GsxSyncAction(double PassengerFraction, double CargoFraction, bool ApplyFuel, string Reason)
{
    public bool WritesPayload => !double.IsNaN(PassengerFraction);
}

/// <summary>
/// Keeps the sim's payload in step with GSX boarding and deboarding: the cabin fills as GSX counts passengers
/// on board, the holds as its cargo percentage grows, and the block fuel from the load sheet is set once GSX
/// refuelling finishes (or at the end of boarding if GSX doesn't refuel). One instance per flight.
/// Created for a job that already departed (after an app restart), it only follows deboarding: GSX may still report
/// the boarding of the departure, and re-boarding or emptying the cabin then would change the load in flight.
/// </summary>
public sealed class GsxLoadSync(bool departed = false)
{
    /// <summary>Smallest change in load fraction worth a SimConnect write.</summary>
    public const double Step = 0.05;

    private double _pax = double.NaN, _cargo = double.NaN;
    private bool _fuelApplied, _boardingSeen;

    public GsxSyncAction? Next(GsxState s)
    {
        if (!s.Detected) return null;
        if (departed && s.Deboarding is not (GsxServiceState.Performing or GsxServiceState.Completed)) return null;

        bool refuelHandled = s.Refueling is GsxServiceState.Requested or GsxServiceState.Performing;
        bool fuelNow = !departed && !_fuelApplied &&
                       (s.Refueling == GsxServiceState.Completed ||
                        (s.Boarding == GsxServiceState.Completed && !refuelHandled));
        if (fuelNow) _fuelApplied = true;

        (double pax, double cargo, string reason) target;
        if (s.Deboarding is GsxServiceState.Performing or GsxServiceState.Completed)
        {
            target = s.Deboarding == GsxServiceState.Completed
                ? (0, 0, "GSX: Deboarding abgeschlossen")
                : (1 - Fraction(s.PassengersDeboarded, s.PassengersDeboardingTotal), 1 - s.CargoDeboardingPercent / 100, "GSX: Deboarding");
        }
        else if (s.Boarding is GsxServiceState.Performing or GsxServiceState.Completed)
        {
            _boardingSeen = true;
            target = s.Boarding == GsxServiceState.Completed
                ? (1, 1, "GSX: Boarding abgeschlossen")
                : (Fraction(s.PassengersBoarded, s.PassengersBoardingTotal), s.CargoBoardingPercent / 100, "GSX: Boarding");
        }
        else if (!_boardingSeen && double.IsNaN(_pax))
        {
            // Empty cabin and holds before GSX starts boarding; crew stays on board.
            target = (0, 0, "GSX: Kabine vor dem Boarding leeren");
        }
        else
        {
            return fuelNow ? new GsxSyncAction(double.NaN, double.NaN, true, "GSX: Betankung abgeschlossen") : null;
        }

        bool final = target.pax is 0 or 1 && target.cargo is 0 or 1;
        bool changed = double.IsNaN(_pax) || Math.Abs(target.pax - _pax) >= Step || Math.Abs(target.cargo - _cargo) >= Step ||
                       (final && (target.pax != _pax || target.cargo != _cargo));
        if (!changed)
            return fuelNow ? new GsxSyncAction(double.NaN, double.NaN, true, "GSX: Betankung abgeschlossen") : null;

        _pax = target.pax;
        _cargo = target.cargo;
        return new GsxSyncAction(target.pax, target.cargo, fuelNow, target.reason);
    }

    /// <summary>
    /// Payload may only be written on the ground: before the job departed (no session yet or preflight), or once it is
    /// parked at the destination. Right after an app restart the phase is unknown (null) until the flight is resumed.
    /// </summary>
    public static bool MayWrite(FlightPhase? phase, bool onGround, bool departed, bool sessionIsThisJob) =>
        onGround && (departed
            ? sessionIsThisJob && phase == FlightPhase.Arrived
            : phase is null or FlightPhase.Preflight);

    private static double Fraction(int done, int total) => total <= 0 ? 0 : Math.Clamp((double)done / total, 0, 1);

    /// <summary>Station loads for the given progress: crew always full, cabin and holds scaled.</summary>
    public static IReadOnlyList<StationLoad> ScaleStations(LoadSheet sheet, double passengerFraction, double cargoFraction)
    {
        var kinds = sheet.Aircraft.Stations.ToDictionary(s => s.Index, s => s.Kind);
        return sheet.Stations.Select(st => kinds.GetValueOrDefault(st.Index, StationKind.Passengers) switch
        {
            StationKind.Crew => st,
            StationKind.Passengers => st with { Mass = st.Mass * passengerFraction },
            _ => st with { Mass = st.Mass * cargoFraction }
        }).ToList();
    }

    /// <summary>
    /// Warns when GSX boards a different number of passengers than the mission has (e.g. no SimBrief OFP loaded).
    /// The OFP counts the crew as passengers (<paramref name="crewAsPassengers"/>), so GSX may include them.
    /// </summary>
    public static string? PassengerMismatch(GsxState s, PassengerCount mission, int crewAsPassengers = 0)
    {
        int gsx = s.PassengersBoardingTotal > 0 ? s.PassengersBoardingTotal : s.Passengers;
        if (gsx <= 0 || gsx == mission.Seated || gsx == mission.Total ||
            gsx == mission.Seated + crewAsPassengers || gsx == mission.Total + crewAsPassengers) return null;
        return $"GSX rechnet mit {gsx} Passagieren, der Auftrag hat {mission.Seated}. " +
               "In GSX „Trust SimBrief“ für Passagiere aktivieren und den passenden OFP erzeugen.";
    }
}
