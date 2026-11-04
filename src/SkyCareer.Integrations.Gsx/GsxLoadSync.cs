using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Loading;
using SkyCareer.Sim.Tracking;
using SkyCareer.Localization;

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
/// The block fuel is only set when nobody else fuelled the aircraft: a GSX refuelling that raised the fuel (GSX fuels to
/// the OFP) or fuel loaded otherwise (EFB, stored fuel restored) is kept, and a refuelling or boarding must have been
/// seen running, because GSX can show a stale "completed" when the sim starts (2026-11-02: the restored fuel was
/// overwritten two seconds after arming).
/// Created for a job that already departed (after an app restart), it only follows deboarding: GSX may still report
/// the boarding of the departure, and re-boarding or emptying the cabin then would change the load in flight.
/// </summary>
public sealed class GsxLoadSync(bool departed = false)
{
    /// <summary>Smallest change in load fraction worth a SimConnect write.</summary>
    public const double Step = 0.05;
    /// <summary>A fuel rise above this counts as fuel loaded by someone else (GSX, EFB, restored fuel).</summary>
    public const double FuelLoadedKg = 20;

    private double _pax = double.NaN, _cargo = double.NaN;
    private bool _fuelApplied, _boardingSeen, _boardingRan, _refuelRan;
    private double? _lowestFuel, _fuelAtRefuel;

    /// <param name="s">The current GSX state.</param>
    /// <param name="fuelKg">Fuel on board now; null when unknown (the block fuel is then set as if nobody fuelled).</param>
    public GsxSyncAction? Next(GsxState s, double? fuelKg = null)
    {
        if (!s.Detected) return null;
        if (departed && s.Deboarding is not (GsxServiceState.Performing or GsxServiceState.Completed)) return null;

        bool refuelHandled = s.Refueling is GsxServiceState.Requested or GsxServiceState.Performing;
        if (refuelHandled && !_refuelRan)
        {
            _refuelRan = true;
            _fuelAtRefuel = fuelKg;
        }
        if (s.Boarding == GsxServiceState.Performing) _boardingRan = true;
        if (fuelKg is { } f && !(_lowestFuel <= f)) _lowestFuel = f;

        // After GSX refuelling only if it didn't change the fuel (an aircraft GSX can't fuel); after boarding only
        // if GSX never refuelled and nobody else loaded fuel since this sync started.
        bool refuelled = _refuelRan && s.Refueling == GsxServiceState.Completed &&
                         !(fuelKg > _fuelAtRefuel + FuelLoadedKg);
        bool boarded = _boardingRan && !_refuelRan && s.Boarding == GsxServiceState.Completed && !refuelHandled &&
                       !(fuelKg > _lowestFuel + FuelLoadedKg);
        bool fuelNow = !departed && !_fuelApplied && (refuelled || boarded);
        // Decided once: a GSX refuelling that fuelled the aircraft ends the question as well.
        if (fuelNow || _refuelRan && s.Refueling == GsxServiceState.Completed) _fuelApplied = true;

        (double pax, double cargo, string reason) target;
        if (s.Deboarding is GsxServiceState.Performing or GsxServiceState.Completed)
        {
            target = s.Deboarding == GsxServiceState.Completed
                ? (0, 0, L.T("GSX: deboarding completed"))
                : (1 - Fraction(s.PassengersDeboarded, s.PassengersDeboardingTotal), 1 - s.CargoDeboardingPercent / 100, L.T("GSX: deboarding"));
        }
        else if (s.Boarding is GsxServiceState.Performing or GsxServiceState.Completed)
        {
            _boardingSeen = true;
            target = s.Boarding == GsxServiceState.Completed
                ? (1, 1, L.T("GSX: boarding completed"))
                : (Fraction(s.PassengersBoarded, s.PassengersBoardingTotal), s.CargoBoardingPercent / 100, L.T("GSX: boarding"));
        }
        else if (!_boardingSeen && double.IsNaN(_pax))
        {
            // Empty cabin and holds before GSX starts boarding; crew stays on board.
            target = (0, 0, L.T("GSX: emptying cabin before boarding"));
        }
        else
        {
            return fuelNow ? new GsxSyncAction(double.NaN, double.NaN, true, L.T("GSX: refuelling completed")) : null;
        }

        bool final = target.pax is 0 or 1 && target.cargo is 0 or 1;
        bool changed = double.IsNaN(_pax) || Math.Abs(target.pax - _pax) >= Step || Math.Abs(target.cargo - _cargo) >= Step ||
                       (final && (target.pax != _pax || target.cargo != _cargo));
        if (!changed)
            return fuelNow ? new GsxSyncAction(double.NaN, double.NaN, true, L.T("GSX: refuelling completed")) : null;

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
    /// The crew never reaches SimBrief as passengers, so GSX counts the passengers only.
    /// </summary>
    public static string? PassengerMismatch(GsxState s, PassengerCount mission)
    {
        int gsx = s.PassengersBoardingTotal > 0 ? s.PassengersBoardingTotal : s.Passengers;
        if (gsx <= 0 || gsx == mission.Seated || gsx == mission.Total) return null;
        return L.F("GSX expects {0} passengers, the job has {1}. Enable “Trust SimBrief” for passengers in GSX and generate the matching OFP.", gsx, mission.Seated);
    }
}
