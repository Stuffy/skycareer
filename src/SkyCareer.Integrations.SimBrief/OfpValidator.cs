namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// What the mission and its load sheet expect from the OFP. Weights in kg. <see cref="Passengers"/> and
/// <see cref="FreightKg"/> are what the dispatch link sent (<see cref="SimBriefPayload"/>). With
/// <see cref="BagsAsFreight"/> (detailed load) SimBrief must not add bags; otherwise its bag weight must equal
/// <see cref="BagWeightKg"/>.
/// </summary>
public sealed record OfpExpectation(string Origin, string Destination, string AircraftIcao, int Passengers, double FreightKg,
    double PayloadKg = 0, double ZeroFuelWeightKg = 0, double ProfileMaxZeroFuelKg = 0,
    double PassengerWeightKg = 0, double BagWeightKg = 0, bool BagsAsFreight = false);

/// <summary>
/// Checks that an imported OFP belongs to the accepted mission and plans the same load. Messages are shown in the
/// (German) UI as-is.
/// </summary>
public static class OfpValidator
{
    public static IReadOnlyList<string> Validate(SimBriefOfp ofp, OfpExpectation e)
    {
        var problems = new List<string>();
        if (!ofp.Origin.Equals(e.Origin, StringComparison.OrdinalIgnoreCase))
            problems.Add($"Abflug im OFP {ofp.Origin}, Auftrag {e.Origin}");
        if (!ofp.Destination.Equals(e.Destination, StringComparison.OrdinalIgnoreCase))
            problems.Add($"Ziel im OFP {ofp.Destination}, Auftrag {e.Destination}");
        if (!ofp.AircraftIcao.Equals(e.AircraftIcao, StringComparison.OrdinalIgnoreCase))
            problems.Add($"Flugzeugtyp im OFP {ofp.AircraftIcao}, Profil {e.AircraftIcao}");

        if (ofp.Passengers != e.Passengers)
            problems.Add(ofp.PlannedPassengers > ofp.Passengers
                ? $"SimBrief hat nur {ofp.Passengers} von {ofp.PlannedPassengers} Passagieren eingeplant (Gewichtslimit), Auftrag: {e.Passengers}"
                : $"OFP mit {ofp.Passengers} Passagieren, Auftrag mit {e.Passengers}");
        const double LbPerKg = 2.20462262;
        if (e.PassengerWeightKg > 0 && ofp.PassengerWeightKg > 0 && Math.Abs(ofp.PassengerWeightKg - e.PassengerWeightKg) > 0.5)
            problems.Add($"SimBrief rechnet mit {ofp.PassengerWeightKg * LbPerKg:N0} lbs je Passagier, die Karriere mit {e.PassengerWeightKg * LbPerKg:N0} lbs " +
                         "– unter „Einstellungen → Beladung“ angleichen (gilt für neue Aufträge)");
        if (e.BagsAsFreight)
        {
            // SkyCareer sends the bags as freight; SimBrief's own bags would count them twice.
            if (ofp.BagWeightKg > 0.5 && ofp.BagCount > 0)
                problems.Add($"Im SimBrief-Airframe ist ein Gepäckgewicht von {ofp.BagWeightKg * LbPerKg:N0} lbs eingestellt – auf 0 setzen, " +
                             "SkyCareer sendet das Gepäck als Fracht (oder „Genaue Beladung an SimBrief“ ausschalten)");
        }
        else
        {
            if (e.BagWeightKg > 0 && ofp.BagWeightKg > 0 && Math.Abs(ofp.BagWeightKg - e.BagWeightKg) > 0.5)
                problems.Add($"SimBrief rechnet mit {ofp.BagWeightKg * LbPerKg:N0} lbs je Gepäckstück, die Karriere mit {e.BagWeightKg * LbPerKg:N0} lbs " +
                             "– unter „Einstellungen → Beladung“ angleichen (gilt für neue Aufträge)");
            if (ofp.PlannedBagCount > ofp.BagCount)
                problems.Add($"SimBrief hat {ofp.PlannedBagCount - ofp.BagCount} von {ofp.PlannedBagCount} Gepäckstücken wegen des Gewichtslimits weggelassen " +
                             $"(MZFW im SimBrief-Airframe {ofp.MaxZeroFuelWeightKg:N0} kg)");
        }
        if (Math.Abs(ofp.FreightKg - e.FreightKg) > Math.Max(50, e.FreightKg * 0.05))
            problems.Add($"Fracht im OFP {ofp.FreightKg:N0} kg, Auftrag {e.FreightKg:N0} kg");
        if (e.PayloadKg > 0 && ofp.PayloadKg > 0 && Math.Abs(ofp.PayloadKg - e.PayloadKg) > Math.Max(50, e.PayloadKg * 0.05))
            problems.Add($"Zuladung (Passagiere, Gepäck, Fracht) im OFP {ofp.PayloadKg:N0} kg, Load-Sheet {e.PayloadKg:N0} kg");
        if (e.ZeroFuelWeightKg > 0 && ofp.ZeroFuelWeightKg > 0 &&
            Math.Abs(ofp.ZeroFuelWeightKg - e.ZeroFuelWeightKg) > Math.Max(150, e.ZeroFuelWeightKg * 0.02))
            problems.Add($"ZFW im OFP {ofp.ZeroFuelWeightKg:N0} kg, Load-Sheet {e.ZeroFuelWeightKg:N0} kg " +
                         $"(Leergewicht im SimBrief-Airframe {ofp.OperatingEmptyKg:N0} kg)");
        if (e.ProfileMaxZeroFuelKg > 0 && ofp.MaxZeroFuelWeightKg > 0 && e.ProfileMaxZeroFuelKg - ofp.MaxZeroFuelWeightKg > 100)
            problems.Add($"MZFW im Flugzeugprofil ({e.ProfileMaxZeroFuelKg:N0} kg) liegt über dem SimBrief-Wert ({ofp.MaxZeroFuelWeightKg:N0} kg) – " +
                         "das Profil erlaubt mehr Zuladung, als das Flugzeug tragen darf");
        return problems;
    }
}
