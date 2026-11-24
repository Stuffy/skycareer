using Microsoft.EntityFrameworkCore;
using SkyCareer.Data;
using SkyCareer.Domain;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Localization;
using SkyCareer.Missions;
using SkyCareer.Missions.Generation;

namespace SkyCareer.Application;

/// <summary>A client with the career's standing, for the client page.</summary>
public sealed record ClientOverview(Client Client, ClientStanding Standing, StoryChain? AvailableChain, StoryStep? NextStep,
    MissionContract? StoryJob, bool CanStartSeries);

public sealed partial class CareerService
{
    /// <summary>Story offers stay on the market this long; they are made again when the aircraft has moved.</summary>
    public static readonly TimeSpan StoryOfferValidity = TimeSpan.FromDays(30);

    public Client? Client(string? id) => id is null ? null : content.Clients.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every client with the career's standing (new clients start at <see cref="ClientStandings.Start"/>), best known first.</summary>
    public async Task<List<ClientOverview>> GetClientsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        if (ExpireSeries(db, career)) await db.SaveChangesAsync();
        var standings = await db.ClientStandings.Where(s => s.CareerId == career.Id).ToListAsync();
        var storyJobs = await db.Contracts.Where(c => c.CareerId == career.Id && c.ChainId != null
                                                      && (c.Status == ContractStatus.Offered || c.Status == ContractStatus.Accepted || c.Status == ContractStatus.InProgress))
            .ToListAsync();
        return content.Clients.Select(client =>
            {
                var s = standings.FirstOrDefault(x => x.ClientId == client.Id) ?? new ClientStanding { CareerId = career.Id, ClientId = client.Id };
                var (chain, step) = NextStoryStep(client, s);
                var job = chain is null ? null : storyJobs.FirstOrDefault(j => j.ClientId == client.Id && j.ChainId == chain.Id);
                return new ClientOverview(client, s, chain, step, job, !s.HasSeries && ClientStandings.Tier(s.Standing) >= ClientTier.Silver);
            })
            .OrderByDescending(o => o.Standing.Standing).ThenBy(o => o.Client.Name).ToList();
    }

    /// <summary>
    /// Starts a series for the client: <see cref="ClientStandings.SeriesTarget"/> of its jobs within
    /// <see cref="ClientStandings.SeriesDays"/> game days pay a bonus on their payouts; missing the date costs standing.
    /// </summary>
    public async Task StartSeriesAsync(string clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var career = await RequireCareer(db);
        if (Client(clientId) is null) throw new CareerException($"Unknown client '{clientId}'.");
        var s = await StandingAsync(db, career, clientId);
        if (s.HasSeries) throw new CareerException(L.T("A series with this client is already running."));
        if (ClientStandings.Tier(s.Standing) < ClientTier.Silver) throw new CareerException(L.T("Series are offered from silver standing."));
        (s.SeriesTarget, s.SeriesFlown, s.SeriesPayouts, s.SeriesEndsAt) = (ClientStandings.SeriesTarget(s.Standing), 0, 0, career.GameTime.AddDays(ClientStandings.SeriesDays));
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    /// <summary>The chain a client offers next and the step of it to fly; null when none is unlocked.</summary>
    public static (StoryChain? Chain, StoryStep? Step) NextStoryStep(Client client, ClientStanding s)
    {
        if (s.ChainId is { } running && client.Chains.FirstOrDefault(c => c.Id == running) is { } current && s.ChainStep < current.Steps.Count)
            return (current, current.Steps[s.ChainStep]);
        var chain = client.Chains.FirstOrDefault(c => !s.Completed(c.Id) && s.Standing >= c.MinStanding && c.Steps.Count > 0);
        return chain is null ? (null, null) : (chain, chain.Steps[0]);
    }

    private static async Task<ClientStanding> StandingAsync(SkyCareerDb db, Career career, string clientId)
    {
        var s = await db.ClientStandings.FirstOrDefaultAsync(x => x.CareerId == career.Id && x.ClientId == clientId);
        if (s is not null) return s;
        s = new ClientStanding { CareerId = career.Id, ClientId = clientId, Standing = ClientStandings.Start };
        db.ClientStandings.Add(s);
        return s;
    }

    /// <summary>Ends series past their date: standing drops; returns whether anything changed.</summary>
    private bool ExpireSeries(SkyCareerDb db, Career career)
    {
        bool changed = false;
        foreach (var s in db.ClientStandings.Where(x => x.CareerId == career.Id && x.SeriesTarget != null).AsEnumerable()
                     .Where(x => x.SeriesEndsAt < career.GameTime))
        {
            s.Standing = Math.Clamp(s.Standing - ClientStandings.SeriesStanding, 0, 100);
            (s.SeriesTarget, s.SeriesFlown, s.SeriesPayouts, s.SeriesEndsAt) = (null, 0, 0, null);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Picks the client of a new offer: one whose mission types and countries fit, weighted by standing, for about
    /// <see cref="ClientStandings.ClientShare"/> of the offers; its tier raises the pay of all legs.
    /// </summary>
    private void AssignClient(MissionOffer offer, IReadOnlyList<MissionContract> legs, IReadOnlyDictionary<string, int> standings, Random draw)
    {
        var fitting = content.Clients.Where(c => c.Offers(offer.Type, offer.Origin, offer.Destination)).ToList();
        if (fitting.Count == 0 || draw.NextDouble() >= ClientStandings.ClientShare) return;
        double total = fitting.Sum(c => standings.GetValueOrDefault(c.Id, ClientStandings.Start) + 10.0);
        double pick = draw.NextDouble() * total;
        var client = fitting.First(c => (pick -= standings.GetValueOrDefault(c.Id, ClientStandings.Start) + 10.0) <= 0 || c == fitting[^1]);
        decimal factor = ClientStandings.PayFactor(standings.GetValueOrDefault(client.Id, ClientStandings.Start));
        foreach (var leg in legs)
        {
            leg.ClientId = client.Id;
            leg.Payout = Math.Round(leg.Payout * factor, 0);
        }
    }

    /// <summary>
    /// Puts the next unlocked story step of every client on the market, for the first aircraft that can fly it from where
    /// it stands (one story offer per client at a time; it doesn't expire like ordinary offers).
    /// </summary>
    private async Task<int> EnsureStoryOffersAsync(SkyCareerDb db, Career career, List<FleetAircraft> fleet, List<MissionContract> offers,
        Dictionary<string, bool?> ilsCache, CancellationToken ct)
    {
        int created = 0;
        var standings = await db.ClientStandings.Where(s => s.CareerId == career.Id).ToListAsync(ct);
        var open = await db.Contracts.Where(c => c.CareerId == career.Id && c.ChainId != null
                                                 && (c.Status == ContractStatus.Offered || c.Status == ContractStatus.Accepted || c.Status == ContractStatus.InProgress))
            .Select(c => c.ClientId).ToListAsync(ct);
        foreach (var client in content.Clients.Where(c => c.Chains.Count > 0 && !open.Contains(c.Id)))
        {
            var s = standings.FirstOrDefault(x => x.ClientId == client.Id) ?? new ClientStanding { CareerId = career.Id, ClientId = client.Id };
            var (chain, step) = NextStoryStep(client, s);
            if (chain is null || step is null || content.MissionTypes.FirstOrDefault(t => t.Id == step.MissionType) is not { } type) continue;
            foreach (var aircraft in fleet.Where(f => f.IsAvailable(career.GameTime)))
            {
                var profile = Profile(aircraft.ProfileId);
                if (!RequirementEvaluator.Evaluate(type, profile).Eligible) continue;
                if ((await db.Airports.FindAsync([aircraft.Location], ct))?.ToDomain() is not { } origin) continue;
                double radius = Math.Min(step.MaxNm ?? type.Distance.MaxNm, RequirementEvaluator.EstimatedRangeNm(profile) * 0.9);
                var airports = step.Destination is { } fixedIdent
                    ? (await db.Airports.FindAsync([fixedIdent.ToUpperInvariant()], ct))?.ToDomain() is { } fixedAirport ? [fixedAirport] : []
                    : (await AirportsWithinAsync(db, origin.Position, radius, profile.MinRunwayM, ilsCache, ct)).Where(a => step.Allows(origin, a)).ToList();
                if (airports.Count == 0) continue;
                // A story step names its own distances and cargo; the type's other rules (load, pay) stay.
                var stepType = type with
                {
                    Distance = new DistanceRule(step.MinNm ?? 1, step.MaxNm ?? Math.Max(type.Distance.MaxNm, 5000)),
                    Cargo = step.CargoKind is { } kind && type.Cargo is { } cargo ? cargo with { Kinds = [kind] } : type.Cargo,
                    ReturnFlight = null, MultiStop = null
                };
                var draw = new Random(StableHash.Of(MarketSeedOf(career), client.Id, chain.Id, s.ChainStep, aircraft.Registration));
                MissionOffer? offer = null;
                for (int attempt = 0; attempt < 10 && offer is null; attempt++)
                    offer = generators.Get(type).TryGenerate(new GenerationContext(stepType, profile, origin, airports, career.GameTime, draw)
                    {
                        Masses = PassengerMassTable.ForCareer(career.PassengerWeightKg, career.BagWeightKg), Reputation = career.Reputation
                    });
                if (offer is null) continue;
                var contract = ToContract(offer, career, aircraft);
                contract.ClientId = client.Id;
                contract.ChainId = chain.Id;
                contract.ChainStep = s.ChainStep;
                contract.ExpiresAt = career.GameTime + StoryOfferValidity;
                contract.Payout = Math.Round(contract.Payout * ClientStandings.PayFactor(s.Standing), 0);
                contract.FlightNumber = NewFlightNumber(career, type, offer, draw);
                db.Contracts.Add(contract);
                offers.Add(contract);
                created++;
                break;
            }
        }
        return created;
    }

    /// <summary>
    /// After a client's flight: standing, series progress and story chain. Returns a line for the settlement panel such as
    /// "Kestrel Mining: standing +2 (silver)" plus the bonus lines to book (series bonus).
    /// </summary>
    private async Task<(string? Note, List<CostLine> Lines)> SettleClientAsync(SkyCareerDb db, Career career, MissionContract c,
        Missions.Settlement.SettlementResult s)
    {
        var lines = new List<CostLine>();
        if (Client(c.ClientId) is not { } client) return (null, lines);
        var standing = await StandingAsync(db, career, client.Id);
        ExpireSeries(db, career);
        bool flown = s.Outcome is FlightOutcome.Completed or FlightOutcome.Late;
        int change = ClientStandings.Change(s.Outcome, s.Score.Total);
        // A customer whose change of plans was turned down remembers it.
        if (c.Event == InFlightEventKind.DestinationChange && c.EventAccepted == false) change -= 1;
        var notes = new List<string>();
        if (flown) standing.FlightsFlown++;

        if (flown && standing.HasSeries)
        {
            standing.SeriesFlown++;
            standing.SeriesPayouts += c.Payout;
            if (standing.SeriesFlown >= standing.SeriesTarget)
            {
                decimal bonus = Math.Round(standing.SeriesPayouts * ClientStandings.SeriesBonus, 0);
                lines.Add(new CostLine(LedgerCategory.Bonus, bonus, L.F("Series bonus {0} ({1} flights)", client.Name, standing.SeriesFlown)));
                change += ClientStandings.SeriesStanding;
                notes.Add(L.F("Series completed: {0} flights.", standing.SeriesFlown));
                (standing.SeriesTarget, standing.SeriesFlown, standing.SeriesPayouts, standing.SeriesEndsAt) = (null, 0, 0, null);
            }
            else notes.Add(L.F("Series {0}/{1}", standing.SeriesFlown, standing.SeriesTarget));
        }

        if (flown && c.ChainId is { } chainId && client.Chains.FirstOrDefault(x => x.Id == chainId) is { } chain && c.ChainStep is { } step)
        {
            standing.ChainId = chain.Id;
            standing.ChainStep = step + 1;
            if (standing.ChainStep >= chain.Steps.Count)
            {
                standing.CompletedChains = string.Join(",", standing.CompletedChains.Split(',', StringSplitOptions.RemoveEmptyEntries).Append(chain.Id).Distinct());
                (standing.ChainId, standing.ChainStep) = (null, 0);
                change += chain.CompletionStanding;
                notes.Add(L.F("Story “{0}” completed.", chain.DisplayTitle));
            }
            else notes.Add(L.F("Story “{0}”: step {1} of {2} done.", chain.DisplayTitle, step + 1, chain.Steps.Count));
        }

        standing.Standing = Math.Clamp(standing.Standing + change, 0, 100);
        string note = L.F("{0}: standing {1}{2} → {3} ({4})", client.Name, change >= 0 ? "+" : "", change, standing.Standing,
            ClientStandings.TierText(ClientStandings.Tier(standing.Standing)));
        return (notes.Count == 0 ? note : $"{note} · {string.Join(" · ", notes)}", lines);
    }

    /// <summary>The story step a contract belongs to, for its card.</summary>
    public StoryStep? StoryStepOf(MissionContract c) =>
        Client(c.ClientId)?.Chains.FirstOrDefault(x => x.Id == c.ChainId) is { } chain && c.ChainStep is { } step && step < chain.Steps.Count
            ? chain.Steps[step] : null;

    public StoryChain? StoryChainOf(MissionContract c) => Client(c.ClientId)?.Chains.FirstOrDefault(x => x.Id == c.ChainId);
}
