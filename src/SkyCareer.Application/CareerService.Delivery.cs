using Microsoft.EntityFrameworkCore;
using SkyCareer.Data;
using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Localization;
using SkyCareer.Missions;

namespace SkyCareer.Application;

/// <summary>How a newly bought or leased aircraft gets to the home base.</summary>
public enum DeliveryMode
{
    /// <summary>The manufacturer flies it over: costs like a ferry flight, the aircraft is unavailable meanwhile.</summary>
    Delivered,
    /// <summary>It waits at the delivery airport and the player flies the delivery job(s) to the home base.</summary>
    PickUp
}

/// <summary>Where a new aircraft would come from and what having it delivered costs; null fields when no delivery is needed.</summary>
/// <param name="Stops">Airports of a self-flown delivery between the delivery airport and the home base (fuel stops).</param>
public sealed record DeliveryQuote(Airport? From, Airport HomeBase, FerryEstimate? Delivery, IReadOnlyList<Airport> Stops)
{
    public bool Needed => From is not null && Delivery is not null;
}

public sealed partial class CareerService
{
    /// <summary>The mission type of self-flown delivery jobs (content/missions/delivery.json).</summary>
    public const string DeliveryMissionType = "delivery";
    /// <summary>Game time allowed to fly a delivery to the home base.</summary>
    public static readonly TimeSpan DeliveryDeadline = TimeSpan.FromDays(14);
    /// <summary>Legs of a self-flown delivery stay within this share of the aircraft's range.</summary>
    public const double DeliveryLegRangeShare = 0.8;

    /// <summary>
    /// Where a new aircraft of <paramref name="profileId"/> comes from: its profile's delivery airport, or nothing when it
    /// has none, the airport isn't in the airport data or it is the home base (the aircraft is then ready there at once).
    /// </summary>
    public async Task<DeliveryQuote> QuoteDeliveryAsync(string profileId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        return await QuoteDeliveryAsync(db, career, Profile(profileId));
    }

    private async Task<DeliveryQuote> QuoteDeliveryAsync(SkyCareerDb db, Career career, AircraftProfile profile)
    {
        var home = await RequireAirport(db, career.HomeBase);
        if (profile.DeliveryAirport is not { Length: > 0 } ident
            || string.Equals(ident, home.Ident, StringComparison.OrdinalIgnoreCase)
            || (await db.Airports.FindAsync(ident.ToUpperInvariant()))?.ToDomain() is not { } from)
            return new DeliveryQuote(null, home, null, []);
        var probe = new FleetAircraft { ProfileId = profile.Id, Registration = "", Location = from.Ident, Ownership = Ownership.Owned };
        var estimate = CostModel.Ferry(EconomyTables.For(career.Difficulty), profile, probe, from, home, career.GameTime);
        var stops = await DeliveryStopsAsync(db, profile, from, home);
        return new DeliveryQuote(from, home, estimate, stops);
    }

    /// <summary>
    /// Fuel stops for flying an aircraft from <paramref name="from"/> to <paramref name="to"/> when that is beyond
    /// <see cref="DeliveryLegRangeShare"/> of its range: each hop to the medium or large airport within reach that is
    /// nearest to the destination. Empty when one leg does it; null-free but possibly short if no stop is found.
    /// </summary>
    private async Task<List<Airport>> DeliveryStopsAsync(SkyCareerDb db, AircraftProfile profile, Airport from, Airport to)
    {
        double hop = RequirementEvaluator.EstimatedRangeNm(profile) * DeliveryLegRangeShare;
        var stops = new List<Airport>();
        var at = from;
        for (int i = 0; i < 12 && at.Position.DistanceNmTo(to.Position) > hop; i++)
        {
            var next = (await AirportsWithinAsync(db, at.Position, hop, profile.MinRunwayM))
                .Where(a => a.Size is AirportSize.Medium or AirportSize.Large && !a.IsMilitary && a.Ident != at.Ident
                            && a.Position.DistanceNmTo(at.Position) <= hop && a.KnownToSim)
                .OrderBy(a => a.Position.DistanceNmTo(to.Position))
                .FirstOrDefault();
            if (next is null || next.Position.DistanceNmTo(to.Position) >= at.Position.DistanceNmTo(to.Position)) break;
            stops.Add(next);
            at = next;
        }
        return stops;
    }

    /// <summary>
    /// Brings a new aircraft to the home base: delivered (ferry costs, unavailable for the ferry time) or waiting at the
    /// delivery airport with accepted delivery job(s) to the home base, one per leg when fuel stops are needed.
    /// </summary>
    private async Task ArrangeDeliveryAsync(SkyCareerDb db, Career career, FleetAircraft aircraft, AircraftProfile profile, DeliveryMode mode)
    {
        var quote = await QuoteDeliveryAsync(db, career, profile);
        if (!quote.Needed || aircraft.Ownership == Ownership.Rented) return;
        var from = quote.From!;
        if (mode == DeliveryMode.Delivered)
        {
            var delivery = quote.Delivery!;
            Book(db, career, LedgerCategory.AircraftPurchase, -Math.Round(delivery.Total, 0),
                L.F("Delivery flight {0} {1} → {2}", aircraft.Registration, from.Ident, quote.HomeBase.Ident), aircraft.Id);
            aircraft.UnavailableUntil = career.GameTime.AddHours(delivery.BlockHours);
            aircraft.UnavailableReason = L.F("Delivery flight to {0}", quote.HomeBase.Ident);
            return;
        }

        aircraft.Location = from.Ident;
        var route = new List<Airport> { from };
        route.AddRange(quote.Stops);
        route.Add(quote.HomeBase);
        var package = route.Count > 2 ? Guid.NewGuid() : (Guid?)null;
        int flightNumber = 9000 + StableHash.Of(aircraft.Registration) % 900;
        for (int leg = 0; leg < route.Count - 1; leg++)
        {
            double dist = route[leg].Position.DistanceNmTo(route[leg + 1].Position);
            var fuel = FuelPlanner.Plan(profile, dist, FlightRules.Vfr, 0);
            db.Contracts.Add(new MissionContract
            {
                CareerId = career.Id, AircraftId = aircraft.Id, MissionTypeId = DeliveryMissionType,
                Origin = route[leg].Ident, Destination = route[leg + 1].Ident, DistanceNm = Math.Round(dist, 1),
                PlannedBlockFuelKg = Math.Round(fuel.Block.Kg), Payout = 0, OfferedAt = career.GameTime,
                ExpiresAt = career.GameTime + DeliveryDeadline, Deadline = career.GameTime + DeliveryDeadline,
                Status = ContractStatus.Accepted, FlightNumber = flightNumber + leg,
                PackageId = package, PackageLeg = package is null ? null : leg + 1, PackageLegs = package is null ? null : route.Count - 1,
                PackageKind = package is null ? null : nameof(Domain.Career.PackageKind.Delivery), PackageBonusShare = package is null ? null : 0
            });
        }
    }
}
