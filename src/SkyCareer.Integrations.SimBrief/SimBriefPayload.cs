using SkyCareer.Domain.Loading;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// What SimBrief is told about a load sheet. The dispatch page only takes a passenger count (weighed with the airframe's
/// passenger weight) and freight. The pilots are part of the SimBrief airframe's empty weight (OEW), the cabin crew is
/// not, so its standard mass always goes in as cargo; pilots are never sent.
/// <list type="bullet">
/// <item>Standard (<c>detailed: false</c>): seated passengers, and freight plus cabin crew as cargo; SimBrief adds one
/// bag per passenger with the airframe's bag weight, as the offers assume.</item>
/// <item>Detailed: seated passengers, and everything their count doesn't cover – checked bags, freight, infants, mass
/// differences to the passenger weight, cabin crew – as cargo, so SimBrief's ZFW equals ours. The airframe's bag weight
/// must be 0, or SimBrief adds bags of its own.</item>
/// </list>
/// <see cref="ExpectedPayloadKg"/> is the payload the OFP should then show.
/// </summary>
public sealed record SimBriefPayload(int Passengers, double CargoKg, double ExpectedPayloadKg)
{
    public static SimBriefPayload For(LoadSheet sheet, double passengerWeightKg, bool detailed = true)
    {
        var aircraft = sheet.Aircraft;
        double cabinCrewKg = aircraft.CrewIncludedInEmptyWeight ? 0 : aircraft.CabinCrewCount * aircraft.CabinCrewMassKg;
        double pilotsKg = aircraft.CrewIncludedInEmptyWeight ? 0 : aircraft.Pilots * aircraft.CrewMassKg;
        int passengers = sheet.Passengers.Seated;
        if (!detailed)
            return new SimBriefPayload(passengers, Math.Round(sheet.CargoMass.Kg + cabinCrewKg), sheet.Payload.Kg + cabinCrewKg);

        double payload = sheet.SimPayload.Kg - pilotsKg;
        return new SimBriefPayload(passengers, Math.Max(0, Math.Round(payload - passengers * passengerWeightKg)), payload);
    }
}
