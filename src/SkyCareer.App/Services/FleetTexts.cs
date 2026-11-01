using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Localization;

namespace SkyCareer.App.Services;

/// <summary>Display texts for fleet aircraft and ledger categories, shared by the fleet, detail and finance pages.</summary>
public static class FleetTexts
{
    public static string Ownership(Ownership o) => o switch
    {
        Domain.Career.Ownership.Owned => L.T("Owned"),
        Domain.Career.Ownership.Leased => L.T("Leased"),
        _ => L.T("Rented")
    };

    public static string DisposeAction(Ownership o) => o == Domain.Career.Ownership.Owned ? L.T("Sell") : L.T("Hand back");

    public static string DisposeQuestion(FleetAircraft f, AircraftProfile p, DateTime gameTime) => f.Ownership switch
    {
        Domain.Career.Ownership.Owned => L.F("Sell {0} for {1}?", f.Registration, L.Eur(FleetEconomics.SaleValue(p, f, gameTime))),
        Domain.Career.Ownership.Leased => L.F("Terminate the lease for {0} early? Fee: {1}.", f.Registration, L.Eur(f.MonthlyRate * 3)),
        _ => L.F("Return rental aircraft {0}?", f.Registration)
    };

    /// <summary>Market value for owned aircraft, otherwise the lease or rent rate.</summary>
    public static string Value(FleetAircraft f, AircraftProfile p, DateTime gameTime) => f.Ownership switch
    {
        Domain.Career.Ownership.Owned => L.Eur(FleetEconomics.MarketValue(p, f, gameTime)),
        Domain.Career.Ownership.Leased => L.F("{0}/month until {1:MM/yyyy}", L.Eur(f.MonthlyRate), f.LeaseEndsAt),
        _ => L.F("{0}/block hour", L.Eur(f.RentPerBlockHour))
    };

    public static string Fuel(FleetAircraft f, AircraftProfile p) =>
        f.FuelKg is { } fuel ? $"{fuel:N0} kg ({fuel / Math.Max(1, p.MaxFuelKg):P0})" : L.T("unknown");

    public static string Category(LedgerCategory c) => c switch
    {
        LedgerCategory.MissionRevenue => L.T("Mission revenue"),
        LedgerCategory.Bonus => L.T("Bonuses"),
        LedgerCategory.Penalty => L.T("Penalties"),
        LedgerCategory.Fuel => L.T("Fuel"),
        LedgerCategory.LandingFee => L.T("Landing fees"),
        LedgerCategory.Handling => L.T("Handling"),
        LedgerCategory.Navigation => L.T("En-route charges"),
        LedgerCategory.Catering => L.T("Catering"),
        LedgerCategory.Crew => L.T("Crew costs"),
        LedgerCategory.Maintenance => L.T("Maintenance"),
        LedgerCategory.Repair => L.T("Repairs"),
        LedgerCategory.Lease => L.T("Lease rates"),
        LedgerCategory.Rent => L.T("Rental fees"),
        LedgerCategory.Insurance => L.T("Insurance"),
        LedgerCategory.AircraftPurchase => L.T("Aircraft purchase"),
        LedgerCategory.AircraftSale => L.T("Aircraft sale"),
        LedgerCategory.StartingCapital => L.T("Starting capital"),
        LedgerCategory.Adjustment => L.T("Adjustments"),
        _ => c.ToString()
    };

    /// <summary>Starting capital and debug corrections aren't earned, so they stay out of results.</summary>
    public static bool IsOperating(LedgerCategory c) => c is not (LedgerCategory.StartingCapital or LedgerCategory.Adjustment);

    public static string Downtime(TimeSpan t) => t.TotalHours < 24 ? $"{t.TotalHours:0} h" : t.TotalDays == 1 ? L.T("1 day") : L.F("{0:0.#} days", t.TotalDays);
}
