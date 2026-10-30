using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;

namespace SkyCareer.App.Services;

/// <summary>German display texts for fleet aircraft and ledger categories, shared by the fleet, detail and finance pages.</summary>
public static class FleetTexts
{
    public static string Ownership(Ownership o) => o switch
    {
        Domain.Career.Ownership.Owned => "Eigentum",
        Domain.Career.Ownership.Leased => "Geleast",
        _ => "Gemietet"
    };

    public static string DisposeAction(Ownership o) => o == Domain.Career.Ownership.Owned ? "Verkaufen" : "Zurückgeben";

    public static string DisposeQuestion(FleetAircraft f, AircraftProfile p, DateTime gameTime) => f.Ownership switch
    {
        Domain.Career.Ownership.Owned => $"{f.Registration} für {Eur(FleetEconomics.SaleValue(p, f, gameTime))} verkaufen?",
        Domain.Career.Ownership.Leased => $"Leasing für {f.Registration} vorzeitig beenden? Gebühr: {Eur(f.MonthlyRate * 3)}.",
        _ => $"Mietflugzeug {f.Registration} zurückgeben?"
    };

    /// <summary>Market value for owned aircraft, otherwise the lease or rent rate.</summary>
    public static string Value(FleetAircraft f, AircraftProfile p, DateTime gameTime) => f.Ownership switch
    {
        Domain.Career.Ownership.Owned => Eur(FleetEconomics.MarketValue(p, f, gameTime)),
        Domain.Career.Ownership.Leased => $"{Eur(f.MonthlyRate)}/Monat bis {f.LeaseEndsAt:MM/yyyy}",
        _ => $"{Eur(f.RentPerBlockHour)}/Blockstunde"
    };

    public static string Fuel(FleetAircraft f, AircraftProfile p) =>
        f.FuelKg is { } fuel ? $"{fuel:N0} kg ({fuel / Math.Max(1, p.MaxFuelKg):P0})" : "unbekannt";

    public static string Category(LedgerCategory c) => c switch
    {
        LedgerCategory.MissionRevenue => "Auftragserlöse",
        LedgerCategory.Bonus => "Boni",
        LedgerCategory.Penalty => "Strafen",
        LedgerCategory.Fuel => "Treibstoff",
        LedgerCategory.LandingFee => "Landegebühren",
        LedgerCategory.Handling => "Abfertigung",
        LedgerCategory.Navigation => "Streckengebühren",
        LedgerCategory.Catering => "Catering",
        LedgerCategory.Crew => "Personal",
        LedgerCategory.Maintenance => "Wartung",
        LedgerCategory.Repair => "Reparaturen",
        LedgerCategory.Lease => "Leasing",
        LedgerCategory.Rent => "Miete",
        LedgerCategory.Insurance => "Versicherung",
        LedgerCategory.AircraftPurchase => "Flugzeugkauf",
        LedgerCategory.AircraftSale => "Flugzeugverkauf",
        LedgerCategory.StartingCapital => "Startkapital",
        LedgerCategory.Adjustment => "Korrekturen",
        _ => c.ToString()
    };

    /// <summary>Starting capital and debug corrections aren't earned, so they stay out of results.</summary>
    public static bool IsOperating(LedgerCategory c) => c is not (LedgerCategory.StartingCapital or LedgerCategory.Adjustment);

    public static string Downtime(TimeSpan t) => t.TotalHours < 24 ? $"{t.TotalHours:0} h" : t.TotalDays == 1 ? "1 Tag" : $"{t.TotalDays:0.#} Tage";

    private static string Eur(decimal amount) => $"{amount:N0} €";
}
