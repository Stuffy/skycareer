using SkyCareer.Localization;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// What the mission and its load sheet expect from the OFP. Weights in kg. <see cref="Passengers"/> and
/// <see cref="FreightKg"/> are what the dispatch link sent (<see cref="SimBriefPayload"/>). With
/// <see cref="BagsAsFreight"/> (detailed load) SimBrief must not add bags; otherwise its bag weight must equal
/// <see cref="BagWeightKg"/>. With <see cref="ManualZfw"/> the link sent the ZFW itself: SimBrief then balances the freight,
/// so freight, payload and the airframe's pax/bag weights don't matter – only passengers, dropped bags and the ZFW do.
/// </summary>
public sealed record OfpExpectation(string Origin, string Destination, string AircraftIcao, int Passengers, double FreightKg,
    double PayloadKg = 0, double ZeroFuelWeightKg = 0, double ProfileMaxZeroFuelKg = 0,
    double PassengerWeightKg = 0, double BagWeightKg = 0, bool BagsAsFreight = false, bool ManualZfw = false);

/// <summary>
/// Checks that an imported OFP belongs to the accepted mission and plans the same load. Messages are localized
/// display texts.
/// </summary>
public static class OfpValidator
{
    public static IReadOnlyList<string> Validate(SimBriefOfp ofp, OfpExpectation e)
    {
        var problems = new List<string>();
        if (!ofp.Origin.Equals(e.Origin, StringComparison.OrdinalIgnoreCase))
            problems.Add(L.F("Departure in OFP {0}, job {1}", ofp.Origin, e.Origin));
        if (!ofp.Destination.Equals(e.Destination, StringComparison.OrdinalIgnoreCase))
            problems.Add(L.F("Destination in OFP {0}, job {1}", ofp.Destination, e.Destination));
        if (!ofp.AircraftIcao.Equals(e.AircraftIcao, StringComparison.OrdinalIgnoreCase))
            problems.Add(L.F("Aircraft type in OFP {0}, profile {1}", ofp.AircraftIcao, e.AircraftIcao));

        if (ofp.Passengers != e.Passengers)
            problems.Add(ofp.PlannedPassengers > ofp.Passengers
                ? L.F("SimBrief only planned {0} of {1} passengers (weight limit), job: {2}", ofp.Passengers, ofp.PlannedPassengers, e.Passengers)
                : L.F("OFP with {0} passengers, job with {1}", ofp.Passengers, e.Passengers));
        const double LbPerKg = 2.20462262;
        if (e.ManualZfw)
        {
            if (ofp.PlannedBagCount > ofp.BagCount)
                problems.Add(L.F("SimBrief dropped {0} of {1} bags due to the weight limit (MZFW in the SimBrief airframe {2:N0} kg)",
                    ofp.PlannedBagCount - ofp.BagCount, ofp.PlannedBagCount, ofp.MaxZeroFuelWeightKg));
        }
        else if (e.PassengerWeightKg > 0 && ofp.PassengerWeightKg > 0 && Math.Abs(ofp.PassengerWeightKg - e.PassengerWeightKg) > 0.5)
            problems.Add(L.F("SimBrief uses {0:N0} lbs per passenger, the career {1:N0} lbs – align them under “Settings → Loading” (applies to new jobs)",
                ofp.PassengerWeightKg * LbPerKg, e.PassengerWeightKg * LbPerKg));
        if (e.ManualZfw)
        {
            // Freight and payload are SimBrief's balancing items here.
        }
        else if (e.BagsAsFreight)
        {
            // SkyCareer sends the bags as freight; SimBrief's own bags would count them twice.
            if (ofp.BagWeightKg > 0.5 && ofp.BagCount > 0)
                problems.Add(L.F("The SimBrief airframe has a bag weight of {0:N0} lbs – set it to 0, SkyCareer sends the baggage as freight (or turn off “Varying bag weights per passenger”)",
                    ofp.BagWeightKg * LbPerKg));
        }
        else
        {
            if (e.BagWeightKg > 0 && ofp.BagWeightKg > 0 && Math.Abs(ofp.BagWeightKg - e.BagWeightKg) > 0.5)
                problems.Add(L.F("SimBrief uses {0:N0} lbs per bag, the career {1:N0} lbs – align them under “Settings → Loading” (applies to new jobs)",
                    ofp.BagWeightKg * LbPerKg, e.BagWeightKg * LbPerKg));
            if (ofp.PlannedBagCount > ofp.BagCount)
                problems.Add(L.F("SimBrief dropped {0} of {1} bags due to the weight limit (MZFW in the SimBrief airframe {2:N0} kg)",
                    ofp.PlannedBagCount - ofp.BagCount, ofp.PlannedBagCount, ofp.MaxZeroFuelWeightKg));
        }
        if (!e.ManualZfw && Math.Abs(ofp.FreightKg - e.FreightKg) > Math.Max(50, e.FreightKg * 0.05))
            problems.Add(L.F("Freight in OFP {0:N0} kg, job {1:N0} kg", ofp.FreightKg, e.FreightKg));
        if (!e.ManualZfw && e.PayloadKg > 0 && ofp.PayloadKg > 0 && Math.Abs(ofp.PayloadKg - e.PayloadKg) > Math.Max(50, e.PayloadKg * 0.05))
            problems.Add(L.F("Payload (passengers, baggage, freight) in OFP {0:N0} kg, load sheet {1:N0} kg", ofp.PayloadKg, e.PayloadKg));
        // A manual ZFW is planned exactly; a difference then means the OFP was made without it (an older dispatch link).
        if (e.ZeroFuelWeightKg > 0 && ofp.ZeroFuelWeightKg > 0 &&
            Math.Abs(ofp.ZeroFuelWeightKg - e.ZeroFuelWeightKg) > (e.ManualZfw ? 20 : Math.Max(150, e.ZeroFuelWeightKg * 0.02)))
            problems.Add(L.F("ZFW in OFP {0:N0} kg, load sheet {1:N0} kg (empty weight in the SimBrief airframe {2:N0} kg)",
                ofp.ZeroFuelWeightKg, e.ZeroFuelWeightKg, ofp.OperatingEmptyKg));
        if (e.ProfileMaxZeroFuelKg > 0 && ofp.MaxZeroFuelWeightKg > 0 && e.ProfileMaxZeroFuelKg - ofp.MaxZeroFuelWeightKg > 100)
            problems.Add(L.F("MZFW in the aircraft profile ({0:N0} kg) is above the SimBrief value ({1:N0} kg) – the profile allows more payload than the aircraft may carry",
                e.ProfileMaxZeroFuelKg, ofp.MaxZeroFuelWeightKg));
        return problems;
    }
}
