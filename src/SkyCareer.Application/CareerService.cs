using Microsoft.EntityFrameworkCore;
using SkyCareer.Data;
using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Missions;
using SkyCareer.Missions.Generation;
using SkyCareer.Missions.Settlement;

namespace SkyCareer.Application;

public sealed class CareerException(string message) : Exception(message);

/// <summary>
/// All career use cases: fleet, mission market, flights, money and game time. Every method uses its own short-lived
/// DbContext so the UI can call it from any component. <see cref="Changed"/> tells the UI to refresh.
/// </summary>
public sealed class CareerService(IDbContextFactory<SkyCareerDb> dbFactory, ContentStore store, MissionGeneratorRegistry generators,
    IAirportIlsLookup? ilsLookup = null)
{
    /// <summary>Always the latest content, so profiles created in the editor are usable right away.</summary>
    private GameContent content => store.Current;

    /// <summary>Offers kept on the market per aircraft location.</summary>
    public const int OffersPerLocation = 15;
    /// <summary>New offers per aircraft for a search with criteria.</summary>
    public const int OffersPerSearch = 20;
    /// <summary>Open offers kept per aircraft; older ones are dropped.</summary>
    public const int MaxOffersPerAircraft = 80;
    /// <summary>Unknown airports checked in the sim per search (each check takes up to ~0.6 s).</summary>
    public const int IlsChecksPerSearch = 25;
    /// <summary>Ground time added after each flight before the next can depart.</summary>
    public static readonly TimeSpan Turnaround = TimeSpan.FromMinutes(30);
    public const int LeaseTermMonths = 36;

    public Guid? ActiveCareerId { get; private set; }
    public event Action? Changed;

    public GameContent Content => content;

    /// <summary>Lets pages refresh after aircraft profiles were added or changed.</summary>
    public void NotifyContentChanged() => Changed?.Invoke();

    public AircraftProfile Profile(string id) =>
        content.Aircraft.FirstOrDefault(a => a.Id == id) ?? throw new CareerException($"Unknown aircraft profile '{id}'.");

    /// <summary>Display name of a profile; falls back to the id for profiles that were removed from the content.</summary>
    public string ProfileName(string id) => content.Aircraft.FirstOrDefault(a => a.Id == id)?.Name ?? id;

    public MissionType MissionType(string id) =>
        content.MissionTypes.FirstOrDefault(m => m.Id == id) ?? throw new CareerException($"Unknown mission type '{id}'.");

    // ---------- Career ----------

    public async Task<Career?> LoadLatestCareerAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = (await db.Careers.ToListAsync()).OrderByDescending(c => c.CreatedAtUtc).FirstOrDefault();
        ActiveCareerId = career?.Id;
        return career;
    }

    public async Task<List<Career>> ListCareersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.Careers.ToListAsync()).OrderByDescending(c => c.CreatedAtUtc).ToList();
    }

    public void SelectCareer(Guid id)
    {
        ActiveCareerId = id;
        Changed?.Invoke();
    }

    /// <summary>Leaves the current career, e.g. to return to the career selection screen.</summary>
    public void ClearSelection()
    {
        ActiveCareerId = null;
        Changed?.Invoke();
    }

    /// <summary>Deletes a career with its fleet, jobs, flights and ledger (cascade).</summary>
    public async Task DeleteCareerAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Careers.Where(c => c.Id == id).ExecuteDeleteAsync();
        if (ActiveCareerId == id) ActiveCareerId = null;
        Changed?.Invoke();
    }

    public async Task<Career> GetCareerAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await RequireCareer(db);
    }

    public async Task<Career> CreateCareerAsync(string name, string airline, string homeBase, Difficulty difficulty, string? simBriefUser, DateTime? startTime = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var home = await db.Airports.FindAsync(homeBase.Trim().ToUpperInvariant())
                   ?? throw new CareerException($"Flughafen {homeBase} nicht gefunden. Sind die Flughafendaten importiert?");

        var now = startTime ?? DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var career = new Career
        {
            Name = name.Trim(),
            AirlineName = airline.Trim(),
            HomeBase = home.Ident,
            Difficulty = difficulty,
            SimBriefUser = string.IsNullOrWhiteSpace(simBriefUser) ? null : simBriefUser.Trim(),
            GameTime = start,
            FixedCostsBookedThrough = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Careers.Add(career);
        Book(db, career, LedgerCategory.StartingCapital, EconomyTables.StartingCash(difficulty), "Startkapital");
        await db.SaveChangesAsync();
        ActiveCareerId = career.Id;
        Changed?.Invoke();
        return career;
    }

    public async Task UpdateSimBriefUserAsync(string? user)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        career.SimBriefUser = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    public async Task UpdateGsxSyncAsync(bool enabled)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        career.GsxSyncEnabled = enabled;
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    public const double MinPassengerWeightKg = 40, MaxPassengerWeightKg = 150, MaxBagWeightKg = 50;

    /// <summary>Sets passenger and bag mass for new offers (should equal the SimBrief airframe); accepted jobs keep their masses.</summary>
    public async Task UpdateLoadWeightsAsync(double passengerKg, double bagKg)
    {
        if (double.IsNaN(passengerKg) || passengerKg < MinPassengerWeightKg || passengerKg > MaxPassengerWeightKg)
            throw new CareerException($"Das Passagiergewicht muss zwischen {MinPassengerWeightKg:N0} und {MaxPassengerWeightKg:N0} kg liegen.");
        if (double.IsNaN(bagKg) || bagKg < 0 || bagKg > MaxBagWeightKg)
            throw new CareerException($"Das Gepäckgewicht muss zwischen 0 und {MaxBagWeightKg:N0} kg liegen.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        career.PassengerWeightKg = Math.Round(passengerKg, 2);
        career.BagWeightKg = Math.Round(bagKg, 2);
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    // ---------- Fleet ----------

    /// <summary>True if any career has an aircraft of this type – such profiles must not be deleted.</summary>
    public async Task<bool> IsProfileInUseAsync(string profileId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Fleet.AnyAsync(f => f.ProfileId == profileId);
    }

    /// <summary>Fleet size per career, for the career selection screen.</summary>
    public async Task<Dictionary<Guid, int>> FleetCountsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Fleet.GroupBy(f => f.CareerId).ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<List<FleetAircraft>> GetFleetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var id = RequireCareerId();
        return (await db.Fleet.Where(f => f.CareerId == id).ToListAsync()).OrderBy(f => f.Registration).ToList();
    }

    public async Task<FleetAircraft> AcquireAircraftAsync(string profileId, Ownership ownership, string? registration = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var profile = Profile(profileId);
        var e = profile.Economics;

        var aircraft = new FleetAircraft
        {
            CareerId = career.Id,
            ProfileId = profile.Id,
            Registration = string.IsNullOrWhiteSpace(registration) ? await NextRegistrationAsync(db, career) : registration.Trim().ToUpperInvariant(),
            Ownership = ownership,
            Location = career.HomeBase,
            AcquiredAt = career.GameTime
        };

        switch (ownership)
        {
            case Ownership.Owned:
                if (career.Cash < e.PurchasePriceNew) throw new CareerException($"Nicht genug Geld: {e.PurchasePriceNew:N0} € benötigt.");
                aircraft.PurchasePrice = e.PurchasePriceNew;
                Book(db, career, LedgerCategory.AircraftPurchase, -e.PurchasePriceNew, $"Kauf {profile.Name} {aircraft.Registration}", aircraft.Id);
                break;
            case Ownership.Leased:
                // Pro-rata rate for the rest of the current month; full months are booked at month boundaries.
                decimal firstMonth = Math.Round(e.LeasePerMonth * (decimal)RemainingMonthFraction(career.GameTime), 0);
                if (career.Cash < firstMonth) throw new CareerException($"Nicht genug Geld für die erste Leasingrate ({firstMonth:N0} €).");
                aircraft.MonthlyRate = e.LeasePerMonth;
                aircraft.LeaseEndsAt = career.GameTime.AddMonths(LeaseTermMonths);
                Book(db, career, LedgerCategory.Lease, -firstMonth, $"Leasing {aircraft.Registration} (anteilig)", aircraft.Id);
                break;
            case Ownership.Rented:
                aircraft.RentPerBlockHour = e.RentPerBlockHour;
                break;
        }

        db.Fleet.Add(aircraft);
        await db.SaveChangesAsync();
        Changed?.Invoke();
        return aircraft;
    }

    /// <summary>Sells an owned aircraft, ends a lease early (3 monthly rates fee) or returns a rental.</summary>
    public async Task<decimal> DisposeAircraftAsync(Guid aircraftId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var aircraft = await RequireAircraft(db, aircraftId);
        if (await db.Contracts.AnyAsync(c => c.AircraftId == aircraftId && (c.Status == ContractStatus.Accepted || c.Status == ContractStatus.InProgress)))
            throw new CareerException("Das Flugzeug hat noch einen angenommenen Auftrag.");

        var profile = Profile(aircraft.ProfileId);
        decimal amount = aircraft.Ownership switch
        {
            Ownership.Owned => FleetEconomics.SaleValue(profile, aircraft, career.GameTime),
            Ownership.Leased => -aircraft.MonthlyRate * 3,
            _ => 0
        };
        if (amount != 0)
            Book(db, career, aircraft.Ownership == Ownership.Owned ? LedgerCategory.AircraftSale : LedgerCategory.Lease, amount,
                aircraft.Ownership == Ownership.Owned ? $"Verkauf {aircraft.Registration}" : $"Vorzeitige Leasingrückgabe {aircraft.Registration}", aircraft.Id);

        await db.Contracts.Where(c => c.AircraftId == aircraftId && c.Status == ContractStatus.Offered).ExecuteDeleteAsync();
        db.Fleet.Remove(aircraft);
        await db.SaveChangesAsync();
        Changed?.Invoke();
        return amount;
    }

    public async Task RepairAsync(Guid aircraftId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var aircraft = await RequireAircraft(db, aircraftId);
        var profile = Profile(aircraft.ProfileId);
        var cost = FleetEconomics.RepairCost(profile, aircraft.Condition);
        if (cost <= 0) return;
        if (career.Cash < cost) throw new CareerException($"Nicht genug Geld für die Reparatur ({cost:N0} €).");

        Book(db, career, LedgerCategory.Repair, -cost, $"Reparatur {aircraft.Registration} ({aircraft.Condition:N0} % → 100 %)", aircraft.Id);
        aircraft.Condition = 100;
        aircraft.UnavailableUntil = career.GameTime.AddDays(1);
        aircraft.UnavailableReason = "Reparatur";
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    /// <summary>Costs of ferrying an aircraft to another airport; throws if the move isn't possible.</summary>
    public async Task<FerryEstimate> EstimateRelocationAsync(Guid aircraftId, string destination)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var (aircraft, profile, from, to) = await CheckRelocationAsync(db, career, aircraftId, destination);
        return CostModel.Ferry(EconomyTables.For(career.Difficulty), profile, aircraft, from, to, career.GameTime);
    }

    /// <summary>
    /// Moves an aircraft to another airport. With <paramref name="ferryFlight"/> a hired crew flies it there: the costs
    /// are booked, hours and wear added and the aircraft is unavailable for the block time. Without, it is just moved
    /// (e.g. the player already flew it there in the sim outside a mission). Open offers from the old location go.
    /// </summary>
    public async Task<FerryEstimate?> RelocateAircraftAsync(Guid aircraftId, string destination, bool ferryFlight)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var (aircraft, profile, from, to) = await CheckRelocationAsync(db, career, aircraftId, destination);

        FerryEstimate? ferry = null;
        if (ferryFlight)
        {
            ferry = CostModel.Ferry(EconomyTables.For(career.Difficulty), profile, aircraft, from, to, career.GameTime);
            if (career.Cash < ferry.Total) throw new CareerException($"Nicht genug Geld für die Überführung ({ferry.Total:N0} €).");
            foreach (var line in ferry.Costs) Book(db, career, line.Category, -line.Amount, line.Description, aircraft.Id);
            aircraft.AirframeHours += ferry.AirHours;
            aircraft.HoursSinceCheck += ferry.AirHours;
            aircraft.Cycles++;
            aircraft.Condition = Math.Max(0, aircraft.Condition - FleetEconomics.ConditionLoss(ferry.AirHours, 0, 1));
            aircraft.UnavailableUntil = career.GameTime + TimeSpan.FromHours(ferry.BlockHours);
            aircraft.UnavailableReason = $"Überführung nach {to.Ident}";
            PerformDueCheck(db, career, aircraft, profile);
        }
        aircraft.Location = to.Ident;
        await db.Contracts.Where(c => c.AircraftId == aircraft.Id && c.Status == ContractStatus.Offered).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        Changed?.Invoke();
        return ferry;
    }

    private async Task<(FleetAircraft, AircraftProfile, Airport, Airport)> CheckRelocationAsync(SkyCareerDb db, Career career,
        Guid aircraftId, string destination)
    {
        var aircraft = await RequireAircraft(db, aircraftId);
        var profile = Profile(aircraft.ProfileId);
        string ident = destination.Trim().ToUpperInvariant();
        if (ident.Length == 0) throw new CareerException("Bitte einen Zielflughafen angeben.");
        if (string.Equals(ident, aircraft.Location, StringComparison.OrdinalIgnoreCase))
            throw new CareerException($"{aircraft.Registration} steht bereits in {ident}.");
        if (!aircraft.IsAvailable(career.GameTime))
            throw new CareerException($"{aircraft.Registration} ist bis {aircraft.UnavailableUntil:g} nicht verfügbar ({aircraft.UnavailableReason ?? "Wartung"}).");
        if (await db.Contracts.AnyAsync(c => c.AircraftId == aircraft.Id && (c.Status == ContractStatus.Accepted || c.Status == ContractStatus.InProgress)))
            throw new CareerException($"{aircraft.Registration} hat angenommene Aufträge ab {aircraft.Location}. Erst fliegen oder stornieren.");

        var from = await RequireAirport(db, aircraft.Location);
        var to = await LoadAirportAsync(db, ident) ?? throw new CareerException($"Flughafen {ident} ist nicht bekannt.");
        if (to.LongestRunwayM < profile.MinRunwayM || !to.Surfaces.Any(profile.Surfaces.Contains))
            throw new CareerException($"{profile.Name} kann in {to.Ident} nicht landen (Piste zu kurz oder ungeeignet).");
        if (to.IsMilitary && !profile.IsMilitary)
            throw new CareerException($"{to.Ident} ist ein Militärflugplatz.");
        double range = RequirementEvaluator.EstimatedRangeNm(profile);
        if (from.Position.DistanceNmTo(to.Position) > range * 0.9)
            throw new CareerException($"{to.Ident} liegt außerhalb der Reichweite ({range * 0.9:N0} nm). Bitte in Etappen überführen.");
        return (aircraft, profile, from, to);
    }

    // ---------- Market & contracts ----------

    public async Task<List<MissionContract>> GetContractsAsync(params ContractStatus[] statuses)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var id = RequireCareerId();
        return await db.Contracts.Where(c => c.CareerId == id && statuses.Contains(c.Status)).ToListAsync();
    }

    /// <summary>Expires old offers and tops up the market for every available aircraft at its current location.</summary>
    public async Task<int> RefreshMarketAsync(Random? rng = null) => (await SearchMarketAsync(MissionSearch.Any, null, rng)).Created;

    /// <summary>
    /// Generates offers matching <paramref name="search"/> for one aircraft (or all available ones). Without
    /// criteria it only tops the market up to <see cref="OffersPerLocation"/>; with criteria it adds up to
    /// <see cref="OffersPerSearch"/> matching offers per aircraft. If ILS is required, unknown candidate airports are
    /// checked in the simulator first and the result is cached.
    /// </summary>
    public async Task<MarketSearchResult> SearchMarketAsync(MissionSearch search, Guid? aircraftId = null, Random? rng = null,
        CancellationToken ct = default)
    {
        rng ??= Random.Shared;
        bool plainRefresh = search == MissionSearch.Any;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var career = await RequireCareer(db);
        ExpireOffers(db, career);
        await db.SaveChangesAsync(ct);

        var fleet = await db.Fleet.Where(f => f.CareerId == career.Id && (aircraftId == null || f.Id == aircraftId)).ToListAsync(ct);
        var offers = await db.Contracts.Where(c => c.CareerId == career.Id && c.Status == ContractStatus.Offered).ToListAsync(ct);
        var ilsCache = await LoadIlsCacheAsync(db, ct);
        int created = 0, ilsChecked = 0;
        bool ilsUnavailable = false;
        var masses = PassengerMassTable.ForCareer(career.PassengerWeightKg, career.BagWeightKg);

        foreach (var aircraft in fleet.Where(f => f.IsAvailable(career.GameTime)))
        {
            int existing = offers.Count(o => o.AircraftId == aircraft.Id && o.Origin == aircraft.Location);
            int wanted = plainRefresh ? OffersPerLocation - existing : OffersPerSearch;
            if (wanted <= 0) continue;

            var profile = Profile(aircraft.ProfileId);
            var types = RequirementEvaluator.AvailableFor(content.MissionTypes, profile)
                .Where(t => t.MinReputation <= career.Reputation && search.AcceptsType(t)).ToList();
            if (types.Count == 0) continue;

            var origin = (await db.Airports.FindAsync([aircraft.Location], ct))?.ToDomain();
            if (origin is null) continue;
            double range = RequirementEvaluator.EstimatedRangeNm(profile);
            double radius = Math.Min(Math.Min(types.Max(t => t.Distance.MaxNm), search.DistanceMaxNm ?? double.MaxValue), range);
            double minRunway = Math.Max(profile.MinRunwayM, search.MinRunwayM ?? 0);
            var airports = await AirportsWithinAsync(db, origin.Position, radius, minRunway, ilsCache, ct);

            if (search.RequireIls)
            {
                var (checkedNow, unavailable) = await CheckIlsAsync(db, airports, origin, range, types, search, ilsCache, ct);
                ilsChecked += checkedNow;
                ilsUnavailable |= unavailable;
                airports = airports.Select(a => ilsCache.TryGetValue(a.Ident, out var ils) ? a with { HasIls = ils } : a).ToList();
            }

            int added = 0;
            var recent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perType = new Dictionary<string, (int Offers, int Large, int Capital)>();
            for (int attempt = 0; attempt < wanted * 6 && added < wanted; attempt++)
            {
                var type = PickWeighted(types, rng);
                var (typeOffers, typeLarge, typeCapital) = perType.GetValueOrDefault(type.Id);
                // Keep the type's minimum shares of capitals and large airports: when the next offer would drop below
                // one, ask for such a destination first and fall back to any destination if none is in reach.
                var rule = type.Destinations;
                bool needCapital = rule.MinCapitalShare > 0 && typeCapital < Math.Ceiling(rule.MinCapitalShare * (typeOffers + 1));
                bool needLarge = rule.MinLargeShare > 0 && typeLarge < Math.Ceiling(rule.MinLargeShare * (typeOffers + 1));
                var ctxBase = new GenerationContext(type, profile, origin, airports, career.GameTime, rng)
                {
                    Search = search, RecentDestinations = recent, Masses = masses
                };
                var generator = generators.Get(type);
                var offer = (needCapital ? generator.TryGenerate(ctxBase with { RequireCapital = true }) : null)
                            ?? (needLarge ? generator.TryGenerate(ctxBase with { RequireLarge = true }) : null)
                            ?? generator.TryGenerate(ctxBase);
                if (offer is null) continue;

                var contract = ToContract(offer, career, aircraft);
                db.Contracts.Add(contract);
                offers.Add(contract);
                recent.Add(offer.Destination.Ident);
                perType[type.Id] = (typeOffers + 1, typeLarge + (offer.Destination.Size == AirportSize.Large ? 1 : 0),
                    typeCapital + (DestinationRule.ServesCapital(offer.Destination) ? 1 : 0));
                added++;
            }
            created += added;

            // Keep the market manageable: drop the oldest untouched offers beyond the cap.
            foreach (var old in offers.Where(o => o.AircraftId == aircraft.Id && o.Status == ContractStatus.Offered)
                         .OrderByDescending(o => o.OfferedAt).Skip(MaxOffersPerAircraft).ToList())
            {
                db.Contracts.Remove(old);
                offers.Remove(old);
            }
        }

        await db.SaveChangesAsync(ct);
        if (created > 0) Changed?.Invoke();
        return new MarketSearchResult(created, ilsChecked, ilsUnavailable);
    }

    /// <summary>
    /// Asks the simulator about candidate airports with unknown ILS status, nearest to the preferred trip length first,
    /// and stores the answers. Returns how many were checked and whether the sim was unavailable.
    /// </summary>
    private async Task<(int Checked, bool Unavailable)> CheckIlsAsync(SkyCareerDb db, List<Airport> airports, Airport origin, double range,
        List<MissionType> types, MissionSearch search, Dictionary<string, bool?> cache, CancellationToken ct)
    {
        double minNm = Math.Max(types.Min(t => t.Distance.MinNm), search.DistanceMinNm ?? 0);
        double maxNm = Math.Min(Math.Min(types.Max(t => t.Distance.MaxNm), search.DistanceMaxNm ?? double.MaxValue), range * 0.9);
        double preferred = StandardMissionGenerator.PreferredDistance(range, minNm, maxNm);

        var unknown = airports
            .Where(a => a.Ident != origin.Ident && !cache.ContainsKey(a.Ident) && search.AcceptsDestination(a with { HasIls = true }))
            .Select(a => (a.Ident, Dist: origin.Position.DistanceNmTo(a.Position)))
            .Where(x => x.Dist >= minNm && x.Dist <= maxNm)
            .OrderBy(x => Math.Abs(Math.Log(Math.Max(1, x.Dist) / Math.Max(1, preferred))))
            .Take(IlsChecksPerSearch)
            .Select(x => x.Ident)
            .ToList();
        if (unknown.Count == 0) return (0, false);
        if (ilsLookup is not { Available: true }) return (0, true);

        var results = await ilsLookup.CheckAsync(unknown, ct);
        foreach (var (ident, info) in results)
        {
            cache[ident] = info?.HasIls;
            db.AirportFacilities.Add(new AirportFacilityCacheRow
            {
                Ident = ident, HasIls = info?.HasIls, IlsRunways = info?.IlsRunways ?? "", CheckedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
        return (results.Count, false);
    }

    public async Task AcceptAsync(Guid contractId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var c = await RequireContract(db, contractId);
        if (c.Status != ContractStatus.Offered) throw new CareerException("Angebot ist nicht mehr verfügbar.");
        if (c.ExpiresAt < career.GameTime) throw new CareerException("Angebot ist abgelaufen.");
        if (await db.Contracts.AnyAsync(x => x.AircraftId == c.AircraftId && (x.Status == ContractStatus.Accepted || x.Status == ContractStatus.InProgress)))
            throw new CareerException("Dieses Flugzeug hat bereits einen angenommenen Auftrag.");
        c.Status = ContractStatus.Accepted;
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    /// <summary>Cancelling an accepted job costs 10 % of the payout and some reputation.</summary>
    public async Task CancelAsync(Guid contractId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var c = await RequireContract(db, contractId);
        if (c.Status is not (ContractStatus.Accepted or ContractStatus.InProgress)) throw new CareerException("Nur angenommene Aufträge können storniert werden.");
        c.Status = ContractStatus.Cancelled;
        Book(db, career, LedgerCategory.Penalty, -Math.Round(c.Payout * 0.1m, 0), $"Stornierung {c.Origin}→{c.Destination}", c.AircraftId);
        career.Reputation = Math.Max(0, career.Reputation - 1);
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    public async Task SetOfpAsync(Guid contractId, double blockFuelKg, double tripFuelKg, string route)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await RequireContract(db, contractId);
        c.OfpBlockFuelKg = blockFuelKg;
        c.OfpTripFuelKg = tripFuelKg;
        c.OfpRoute = route;
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    public async Task StartFlightAsync(Guid contractId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var c = await RequireContract(db, contractId);
        if (c.Status != ContractStatus.Accepted) throw new CareerException("Auftrag muss zuerst angenommen werden.");
        var aircraft = await RequireAircraft(db, c.AircraftId);
        if (aircraft.Location != c.Origin) throw new CareerException($"{aircraft.Registration} steht in {aircraft.Location}, nicht in {c.Origin}.");
        if (!aircraft.IsAvailable(career.GameTime)) throw new CareerException($"{aircraft.Registration} ist bis {aircraft.UnavailableUntil:g} nicht verfügbar ({aircraft.UnavailableReason ?? "Wartung"}).");
        c.Status = ContractStatus.InProgress;
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    /// <summary>The planned load sheet, rebuilt from the masses frozen into the contract (and the OFP fuel if imported).</summary>
    public LoadSheet BuildLoadSheet(MissionContract c, AircraftProfile profile)
    {
        var type = MissionType(c.MissionTypeId);
        var rules = type.FlightRules == FlightRulesRequirement.Vfr ? FlightRules.Vfr : FlightRules.Ifr;
        FuelPlan fuel = c.OfpBlockFuelKg is { } block
            ? new FuelPlan(Mass.FromKg(profile.TaxiFuelKg), Mass.FromKg(c.OfpTripFuelKg ?? 0), Mass.Zero, Mass.Zero,
                Mass.FromKg(Math.Max(0, block - profile.TaxiFuelKg - (c.OfpTripFuelKg ?? 0))), Mass.Zero)
            : FuelPlanner.Plan(profile, c.DistanceNm, rules, rules == FlightRules.Ifr ? Math.Min(100, c.DistanceNm * 0.3) : 0);
        var pax = new PassengerCount(c.Adults, c.Children, c.Infants);
        return LoadSheetBuilder.FromMasses(profile, pax, Mass.FromKg(c.PassengerMassKg), Mass.FromKg(c.BaggageKg), Mass.FromKg(c.CargoKg), fuel);
    }

    // ---------- Flight completion ----------

    public async Task<SettlementResult> CompleteFlightAsync(Guid contractId, FlightResult result, string? trackFile = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        var c = await RequireContract(db, contractId);
        if (c.Status != ContractStatus.InProgress) throw new CareerException("Für diesen Auftrag läuft kein Flug.");
        var aircraft = await RequireAircraft(db, c.AircraftId);
        var profile = Profile(aircraft.ProfileId);
        var type = MissionType(c.MissionTypeId);
        var origin = await RequireAirport(db, c.Origin);
        var destination = await RequireAirport(db, c.Destination);
        var nearest = await NearestAirportAsync(db, result.FinalPosition);
        var departure = career.GameTime;

        var s = FlightSettlement.Settle(new SettlementInput(type, c, profile, aircraft, origin, destination, nearest, result,
            departure, EconomyTables.For(career.Difficulty)));

        var flight = new FlightRecord
        {
            CareerId = career.Id,
            AircraftId = aircraft.Id,
            ContractId = c.Id,
            MissionTypeId = type.Id,
            Origin = c.Origin,
            Destination = c.Destination,
            ArrivedAt = s.FinalLocation,
            DepartedGameTime = departure,
            ArrivedGameTime = s.ArrivalGameTime,
            BlockHours = s.BlockHours,
            AirHours = s.AirHours,
            DistanceNm = result.DistanceFlownNm,
            FuelUsedKg = result.FuelUsedKg,
            TouchdownFpm = result.TouchdownFpm,
            MaxGForce = result.MaxGForce,
            Score = s.Score.Total,
            Revenue = s.Revenue,
            Costs = s.Costs,
            Outcome = s.Outcome,
            Notes = string.Join("\n", s.Notes),
            TrackFile = trackFile
        };
        db.Flights.Add(flight);

        career.GameTime = s.ArrivalGameTime;
        foreach (var line in s.Lines) Book(db, career, line.Category, line.Amount, line.Description, aircraft.Id, flight.Id);
        career.Reputation = Math.Clamp(career.Reputation + s.ReputationChange, 0, 100);

        aircraft.Location = s.FinalLocation;
        aircraft.AirframeHours += s.AirHours;
        aircraft.HoursSinceCheck += s.AirHours;
        if (s.AirHours > 0) aircraft.Cycles++;
        aircraft.Condition = Math.Max(0, aircraft.Condition - s.ConditionLoss);
        PerformDueCheck(db, career, aircraft, profile);

        c.Status = s.Outcome is FlightOutcome.Completed or FlightOutcome.Late ? ContractStatus.Completed : ContractStatus.Failed;

        career.GameTime += Turnaround;
        BookFixedCosts(db, career, await db.Fleet.Where(f => f.CareerId == career.Id).ToListAsync());
        ExpireOffers(db, career);
        await db.SaveChangesAsync();
        Changed?.Invoke();
        return s;
    }

    /// <summary>Lets game time pass without flying (e.g. waiting for maintenance or new offers).</summary>
    public async Task WaitAsync(TimeSpan duration)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        career.GameTime += duration;
        var fleet = await db.Fleet.Where(f => f.CareerId == career.Id).ToListAsync();
        BookFixedCosts(db, career, fleet);
        ExpireOffers(db, career);
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    // ---------- Finance & logbook ----------

    public async Task<List<LedgerEntry>> GetLedgerAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var id = RequireCareerId();
        return (await db.Ledger.Where(l => l.CareerId == id).ToListAsync()).OrderByDescending(l => l.GameTime).ToList();
    }

    public async Task<List<FlightRecord>> GetFlightsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var id = RequireCareerId();
        return (await db.Flights.Where(f => f.CareerId == id).ToListAsync()).OrderByDescending(f => f.ArrivedGameTime).ToList();
    }

    // ---------- Airports ----------

    public async Task<int> AirportCountAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Airports.CountAsync();
    }

    public async Task<Airport?> FindAirportAsync(string ident)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LoadAirportAsync(db, ident.Trim().ToUpperInvariant());
    }

    /// <summary>Airports by ident, including ILS status where the simulator has been asked before.</summary>
    public async Task<Dictionary<string, Airport>> GetAirportsAsync(IEnumerable<string> idents)
    {
        var keys = idents.Select(i => i.ToUpperInvariant()).Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Airports.Where(a => keys.Contains(a.Ident)).ToListAsync();
        var ils = await db.AirportFacilities.Where(f => keys.Contains(f.Ident)).ToDictionaryAsync(f => f.Ident, f => f.HasIls);
        return rows.ToDictionary(r => r.Ident, r => r.ToDomain(ils.GetValueOrDefault(r.Ident)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True if the airport data predates the lighting/scheduled-service/paved-length columns, the city matching or the military flag.</summary>
    public async Task<bool> AirportDataOutdatedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Airports.AnyAsync()
               && (!await db.Airports.AnyAsync(a => a.HasLightedRunway || a.LongestPavedRunwayM > 0)
                   || !await db.Airports.AnyAsync(a => a.CityPopulation > 0)
                   || !await db.Airports.AnyAsync(a => a.IsMilitary));
    }

    public async Task<int> ImportAirportsAsync(TextReader airportsCsv, TextReader runwaysCsv, GeoNamesCities? cities = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        int n = await OurAirportsImporter.ImportAsync(db, airportsCsv, runwaysCsv, cities, ct);
        Changed?.Invoke();
        return n;
    }

    // ---------- Internals ----------

    /// <summary>
    /// Books monthly fixed costs (lease rate, insurance) for every month boundary passed, then hands back
    /// aircraft whose lease has ended.
    /// </summary>
    private void BookFixedCosts(SkyCareerDb db, Career career, List<FleetAircraft> fleet)
    {
        var next = career.FixedCostsBookedThrough.AddMonths(1);
        while (next <= career.GameTime)
        {
            foreach (var a in fleet.Where(a => a.AcquiredAt < next && (a.LeaseEndsAt is null || a.LeaseEndsAt > next.AddMonths(-1))))
            {
                // Aircraft whose profile was removed from content still exist in the save; they just cost nothing.
                var profile = content.Aircraft.FirstOrDefault(p => p.Id == a.ProfileId);
                if (profile is null) continue;
                if (a.Ownership == Ownership.Leased)
                    BookAt(db, career, next, LedgerCategory.Lease, -a.MonthlyRate, $"Leasingrate {a.Registration} {next:MM/yyyy}", a.Id);
                if (a.Ownership != Ownership.Rented && profile.Economics.InsurancePerMonth > 0)
                    BookAt(db, career, next, LedgerCategory.Insurance, -profile.Economics.InsurancePerMonth, $"Versicherung {a.Registration} {next:MM/yyyy}", a.Id);
            }
            career.FixedCostsBookedThrough = next;
            next = next.AddMonths(1);
        }

        var busy = db.Contracts.Where(c => c.CareerId == career.Id && (c.Status == ContractStatus.Accepted || c.Status == ContractStatus.InProgress))
            .Select(c => c.AircraftId).ToHashSet();
        foreach (var ended in fleet.Where(a => a.Ownership == Ownership.Leased && a.LeaseEndsAt <= career.GameTime && !busy.Contains(a.Id)))
            db.Fleet.Remove(ended);
    }

    private void PerformDueCheck(SkyCareerDb db, Career career, FleetAircraft aircraft, AircraftProfile profile)
    {
        if (aircraft.Ownership == Ownership.Rented || aircraft.HoursSinceCheck < FleetEconomics.CheckIntervalHours(profile)) return;
        Book(db, career, LedgerCategory.Maintenance, -FleetEconomics.CheckCost(profile), $"Planmäßiger Check {aircraft.Registration}", aircraft.Id);
        aircraft.HoursSinceCheck = 0;
        aircraft.Condition = Math.Min(100, aircraft.Condition + 15);
        aircraft.UnavailableUntil = career.GameTime + FleetEconomics.CheckDowntime(profile);
        aircraft.UnavailableReason = "Planmäßiger Check";
    }

    private static void ExpireOffers(SkyCareerDb db, Career career)
    {
        foreach (var c in db.Contracts.Where(c => c.CareerId == career.Id && c.Status == ContractStatus.Offered).AsEnumerable()
                     .Where(c => c.ExpiresAt < career.GameTime))
            c.Status = ContractStatus.Expired;
    }

    private static void Book(SkyCareerDb db, Career career, LedgerCategory category, decimal amount, string description,
        Guid? aircraftId = null, Guid? flightId = null) =>
        BookAt(db, career, career.GameTime, category, amount, description, aircraftId, flightId);

    private static void BookAt(SkyCareerDb db, Career career, DateTime time, LedgerCategory category, decimal amount, string description,
        Guid? aircraftId = null, Guid? flightId = null)
    {
        career.Cash += amount;
        db.Ledger.Add(new LedgerEntry
        {
            CareerId = career.Id, GameTime = time, Category = category, Amount = amount,
            Description = description, AircraftId = aircraftId, FlightId = flightId
        });
    }

    private static MissionContract ToContract(MissionOffer o, Career career, FleetAircraft aircraft) => new()
    {
        CareerId = career.Id,
        AircraftId = aircraft.Id,
        MissionTypeId = o.Type.Id,
        Origin = o.Origin.Ident,
        Destination = o.Destination.Ident,
        DistanceNm = Math.Round(o.DistanceNm, 1),
        Adults = o.Passengers.Adults,
        Children = o.Passengers.Children,
        Infants = o.Passengers.Infants,
        PassengerMassKg = Math.Round(o.PlannedLoad.PassengerMass.Kg, 1),
        BaggageKg = Math.Round(o.PlannedLoad.BaggageMass.Kg, 1),
        CargoKg = o.Cargo.Kg,
        CargoKind = o.CargoKind,
        PlannedBlockFuelKg = Math.Round(o.PlannedLoad.Fuel.Block.Kg),
        Payout = o.Payout.Amount,
        OfferedAt = career.GameTime,
        ExpiresAt = o.ExpiresAt.UtcDateTime,
        Deadline = o.Deadline.UtcDateTime
    };

    private static MissionType PickWeighted(List<MissionType> types, Random rng)
    {
        double total = types.Sum(t => Math.Max(0.01, t.Weight));
        double roll = rng.NextDouble() * total;
        foreach (var t in types)
        {
            roll -= Math.Max(0.01, t.Weight);
            if (roll <= 0) return t;
        }
        return types[^1];
    }

    private static async Task<List<Airport>> AirportsWithinAsync(SkyCareerDb db, GeoPosition center, double radiusNm, double minRunwayM,
        IReadOnlyDictionary<string, bool?>? ilsCache = null, CancellationToken ct = default)
    {
        double dLat = radiusNm / 60.0;
        double dLon = Math.Min(180, radiusNm / (60.0 * Math.Max(0.05, Math.Cos(center.Latitude * Math.PI / 180))));
        double minLat = center.Latitude - dLat, maxLat = center.Latitude + dLat;
        var rows = await db.Airports
            .Where(a => a.Latitude >= minLat && a.Latitude <= maxLat && a.LongestRunwayM >= minRunwayM)
            .ToListAsync(ct);
        // Longitude filtered in memory so boxes crossing the antimeridian still work.
        return rows.Where(a => dLon >= 180 || LonDiff(a.Longitude, center.Longitude) <= dLon)
            .Select(a => a.ToDomain(ilsCache is not null && ilsCache.TryGetValue(a.Ident, out var ils) ? ils : null))
            .ToList();
    }

    /// <summary>All ILS results learned from the simulator so far (small table; null = sim didn't know the airport).</summary>
    private static async Task<Dictionary<string, bool?>> LoadIlsCacheAsync(SkyCareerDb db, CancellationToken ct) =>
        await db.AirportFacilities.ToDictionaryAsync(f => f.Ident, f => f.HasIls, StringComparer.OrdinalIgnoreCase, ct);

    private static async Task<Airport?> LoadAirportAsync(SkyCareerDb db, string ident)
    {
        var row = await db.Airports.FindAsync(ident);
        if (row is null) return null;
        var ils = await db.AirportFacilities.FindAsync(ident);
        return row.ToDomain(ils?.HasIls);
    }

    private static double LonDiff(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static async Task<Airport?> NearestAirportAsync(SkyCareerDb db, GeoPosition p)
    {
        var near = await AirportsWithinAsync(db, p, 15, 0);
        return near.OrderBy(a => a.Position.DistanceNmTo(p)).FirstOrDefault();
    }

    /// <summary>Registration in the style of the home base's country (D-SCAA, OE-SCAA, N100SC, …).</summary>
    private static async Task<string> NextRegistrationAsync(SkyCareerDb db, Career career)
    {
        string? prefix = career.HomeBase.Length < 2 ? null : career.HomeBase[..2] switch
        {
            "ED" or "ET" => "D-", "LO" => "OE-", "LS" => "HB-", "EG" => "G-", "LF" => "F-", "EH" => "PH-", "EB" => "OO-", _ => null
        };
        int n = await db.Fleet.CountAsync(f => f.CareerId == career.Id);
        while (true)
        {
            string reg = prefix is null
                ? $"N{100 + n}SC"
                : $"{prefix}SC{(char)('A' + n / 26 % 26)}{(char)('A' + n % 26)}";
            if (!await db.Fleet.AnyAsync(f => f.CareerId == career.Id && f.Registration == reg)) return reg;
            n++;
        }
    }

    private static double RemainingMonthFraction(DateTime t)
    {
        var monthStart = new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind);
        var next = monthStart.AddMonths(1);
        return (next - t).TotalHours / (next - monthStart).TotalHours;
    }

    private Guid RequireCareerId() => ActiveCareerId ?? throw new CareerException("Keine Karriere aktiv.");

    private async Task<Career> RequireCareer(SkyCareerDb db) =>
        await db.Careers.FindAsync(RequireCareerId()) ?? throw new CareerException("Karriere nicht gefunden.");

    private async Task<FleetAircraft> RequireAircraft(SkyCareerDb db, Guid id) =>
        await db.Fleet.FirstOrDefaultAsync(f => f.Id == id && f.CareerId == RequireCareerId()) ?? throw new CareerException("Flugzeug nicht gefunden.");

    private async Task<MissionContract> RequireContract(SkyCareerDb db, Guid id) =>
        await db.Contracts.FirstOrDefaultAsync(c => c.Id == id && c.CareerId == RequireCareerId()) ?? throw new CareerException("Auftrag nicht gefunden.");

    private static async Task<Airport> RequireAirport(SkyCareerDb db, string ident) =>
        await LoadAirportAsync(db, ident) ?? throw new CareerException($"Flughafen {ident} nicht in der Datenbank.");
}
