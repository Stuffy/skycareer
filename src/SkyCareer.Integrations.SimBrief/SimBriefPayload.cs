using SkyCareer.Domain.Loading;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// What SimBrief is told about a load sheet. The dispatch page only takes a passenger count (weighed with the airframe's
/// passenger weight) and freight.
/// <list type="bullet">
/// <item>Standard (<c>detailed: false</c>): passengers and freight as they are; SimBrief adds one bag per passenger
/// with the airframe's bag weight, as the offers assume. The crew is not sent.</item>
/// <item>Detailed: crew members go in as passengers, and everything the passenger count doesn't cover – checked bags,
/// freight, infants, the crew's standard masses above or below the passenger weight – goes in as cargo, so SimBrief's
/// ZFW equals ours. The airframe's bag weight must be 0, or SimBrief adds bags of its own.</item>
/// </list>
/// </summary>
public sealed record SimBriefPayload(int Passengers, int CrewAsPassengers, double CargoKg)
{
    public static SimBriefPayload For(LoadSheet sheet, double passengerWeightKg, bool detailed = true)
    {
        if (!detailed) return new SimBriefPayload(sheet.Passengers.Seated, 0, Math.Round(sheet.CargoMass.Kg));

        var aircraft = sheet.Aircraft;
        int crew = aircraft.CrewIncludedInEmptyWeight ? 0 : aircraft.Pilots + aircraft.CabinCrewCount;
        int passengers = sheet.Passengers.Seated + crew;
        double cargo = sheet.SimPayload.Kg - passengers * passengerWeightKg;
        return new SimBriefPayload(passengers, crew, Math.Max(0, Math.Round(cargo)));
    }
}
