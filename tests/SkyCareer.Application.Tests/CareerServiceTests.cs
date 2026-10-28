using SkyCareer.Domain.Loading;
using Microsoft.EntityFrameworkCore;
using SkyCareer.Data;
using SkyCareer.Domain;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions;
using SkyCareer.Missions.Departure;
using SkyCareer.Missions.Generation;
using SkyCareer.Sim;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Application.Tests;

public sealed class CareerServiceTests : IAsyncLifetime
{
    private const string AirportsCsv = """
        "id","ident","type","name","latitude_deg","longitude_deg","elevation_ft","continent","iso_country","iso_region","municipality","scheduled_service"
        1,"EDDF","large_airport","Frankfurt am Main Airport",50.033333,8.570556,364,"EU","DE","DE-HE","Frankfurt","yes"
        2,"EDDM","large_airport","Munich Airport",48.353802,11.7861,1487,"EU","DE","DE-BY","Munich","yes"
        3,"EDDS","large_airport","Stuttgart Airport",48.689899,9.22196,1276,"EU","DE","DE-BW","Stuttgart","yes"
        4,"EDDN","large_airport","Nuremberg Airport",49.498699,11.078056,1046,"EU","DE","DE-BY","Nuremberg","yes"
        5,"EDFE","small_airport","Frankfurt-Egelsbach Airport",49.960833,8.643611,384,"EU","DE","DE-HE","Egelsbach","no"
        6,"EDGZ","small_airport","Weinheim/Bergstraße Airport",49.567501,8.610833,318,"EU","DE","DE-BW","Weinheim","no"
        7,"EDXX","closed","Old Field",50.1,8.6,300,"EU","DE","DE-HE","Nowhere","no"
        8,"EDNR","small_airport","No Runway Field",50.2,8.7,300,"EU","DE","DE-HE","Nowhere","no"
        9,"ETAR","medium_airport","Ramstein Air Base",49.436901,7.60028,776,"EU","DE","DE-RP","Ramstein","no"
        10,"EDDB","large_airport","Berlin Brandenburg Airport",52.351389,13.493889,157,"EU","DE","DE-BR","Berlin","yes"
        """;

    private const string RunwaysCsv = """
        "id","airport_ref","airport_ident","length_ft","width_ft","surface","lighted","closed","le_ident"
        1,1,"EDDF",13123,148,"ASP",1,0,"07C"
        2,2,"EDDM",13123,197,"CON",1,0,"08L"
        3,3,"EDDS",10974,148,"ASP",1,0,"07"
        4,4,"EDDN",8858,148,"ASP",1,0,"10"
        5,5,"EDFE",4593,59,"ASPH-G",1,0,"09"
        6,6,"EDGZ",2526,66,"GRS",0,0,"07"
        7,7,"EDXX",3000,60,"TURF",0,1,"18"
        8,9,"ETAR",10476,148,"ASP",1,0,"08"
        9,10,"EDDB",11811,148,"ASP",1,0,"07R"
        """;

    // GeoNames columns: id, name, ascii, alternates, lat, lon, class, code, country, cc2, admin1-4, population, …
    // GeoNames is tab-separated; written with '|' here for readability.
    private static readonly string CitiesTsv = """
        2925533|Frankfurt am Main|Frankfurt am Main||50.11552|8.68417|P|PPLA2|DE||05|064|06412|06412000|763380||100|Europe/Berlin|2024-01-01
        2867714|München|Munich||48.13743|11.57549|P|PPLA|DE||02|091|09162|09162000|1260391||524|Europe/Berlin|2024-01-01
        2950159|Berlin|Berlin||52.52437|13.41053|P|PPLC|DE||16|00|11000|11000000|3426354||74|Europe/Berlin|2024-01-01
        2878270|Leer|Leer||53.2|7.4|A|ADM4|DE||||||30000|||Europe/Berlin|2024-01-01
        """.Replace('|', '\t');

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"skycareer-test-{Guid.NewGuid():N}.db");
    private CareerService _svc = null!;
    private TestDbFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestDbFactory(SkyCareerDb.Options(_dbPath));
        await using (var db = _factory.CreateDbContext()) await db.Database.MigrateAsync();

        var content = ContentLoader.Load(FindContentRoot());
        _svc = new CareerService(_factory, new ContentStore(content), new MissionGeneratorRegistry([new StandardMissionGenerator()]));
        await _svc.ImportAirportsAsync(new StringReader(AirportsCsv), new StringReader(RunwaysCsv), GeoNamesCities.Read(new StringReader(CitiesTsv)));
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    private Task<Career> NewCareer(Difficulty difficulty = Difficulty.Normal) =>
        _svc.CreateCareerAsync("Test", "Test Air", "EDDF", difficulty, null, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Importer_keeps_usable_airports_and_maps_surfaces()
    {
        Assert.Equal(8, await _svc.AirportCountAsync());
        Assert.True((await _svc.FindAirportAsync("ETAR"))!.IsMilitary);
        Assert.False((await _svc.FindAirportAsync("EDDF"))!.IsMilitary);
        var egelsbach = await _svc.FindAirportAsync("edfe");
        Assert.NotNull(egelsbach);
        Assert.Equal(Domain.Aircraft.RunwaySurface.Paved, Assert.Single(egelsbach.Surfaces));
        Assert.Equal(1400, egelsbach.LongestRunwayM, 0);
        Assert.Equal(Domain.Aircraft.RunwaySurface.Grass, Assert.Single((await _svc.FindAirportAsync("EDGZ"))!.Surfaces));
        Assert.Null(await _svc.FindAirportAsync("EDXX"));
        Assert.Null(await _svc.FindAirportAsync("EDNR"));

        var eddf = (await _svc.FindAirportAsync("EDDF"))!;
        Assert.True(eddf.ScheduledService);
        Assert.True(eddf.HasLightedRunway);
        Assert.Equal(4000, eddf.LongestPavedRunwayM, 0);
        Assert.Null(eddf.HasIls);
        var weinheim = (await _svc.FindAirportAsync("EDGZ"))!;
        Assert.False(weinheim.HasLightedRunway);
        Assert.False(weinheim.ScheduledService);
        Assert.Equal(0, weinheim.LongestPavedRunwayM);
        Assert.False(await _svc.AirportDataOutdatedAsync());
    }

    [Theory]
    [InlineData("Ramstein Air Base", "", false, true)]
    [InlineData("Celle Army Airfield", "", false, true)]
    [InlineData("RAF Lakenheath", "", false, true)]
    [InlineData("Some Field", "Fliegerhorst Lechfeld", false, true)]
    [InlineData("Baghdad International Airport / New Al Muthana Air Base", "", true, false)]
    [InlineData("Frankfurt am Main Airport", "", true, false)]
    [InlineData("Nassau Lynden Pindling International Airport", "", false, false)]
    [InlineData("Graftonbury Field", "", false, false)]
    public void Military_fields_are_detected_by_name(string name, string keywords, bool scheduled, bool expected) =>
        Assert.Equal(expected, OurAirportsImporter.IsMilitaryField(name, keywords, scheduled));

    [Fact]
    public async Task Importer_matches_airports_to_the_cities_they_serve()
    {
        var eddf = (await _svc.FindAirportAsync("EDDF"))!;
        Assert.Equal("Frankfurt am Main", eddf.City);
        Assert.Equal(763380, eddf.CityPopulation);
        Assert.False(eddf.IsCapital);

        // MUC is 28 km out of town and still counts fully; Stuttgart has no city in the list.
        Assert.Equal("München", (await _svc.FindAirportAsync("EDDM"))!.City);
        var edds = (await _svc.FindAirportAsync("EDDS"))!;
        Assert.Equal("", edds.City);
        Assert.Equal(0, edds.CityPopulation);

        // Weinheim is ~63 km from Frankfurt: the population fades with distance.
        var weinheim = (await _svc.FindAirportAsync("EDGZ"))!;
        Assert.InRange(weinheim.CityPopulation, 1, 763380 / 2);
    }

    [Fact]
    public async Task Capital_flag_and_outdated_data_without_cities()
    {
        var cities = GeoNamesCities.Read(new StringReader(CitiesTsv));
        Assert.Equal(3, cities.Count); // the admin area (class A) is skipped
        var ber = cities.Find(52.3667, 13.5033)!.Value; // BER is about 18 km from the centre
        Assert.True(ber.IsCapital);
        Assert.Equal("Berlin", ber.City);
        Assert.Null(cities.Find(0, 0));

        await _svc.ImportAirportsAsync(new StringReader(AirportsCsv), new StringReader(RunwaysCsv));
        Assert.True(await _svc.AirportDataOutdatedAsync());
    }

    [Fact]
    public async Task Ils_search_checks_unknown_airports_once_and_only_offers_ils_destinations()
    {
        var lookup = new FakeIlsLookup(new() { ["EDDM"] = true, ["EDDS"] = false, ["EDDN"] = true });
        var svc = new CareerService(_factory, new ContentStore(ContentLoader.Load(FindContentRoot())),
            new MissionGeneratorRegistry([new StandardMissionGenerator()]), lookup);
        await svc.CreateCareerAsync("Test", "Test Air", "EDDF", Difficulty.Normal, null, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));
        await svc.AcquireAircraftAsync("c208b", Ownership.Rented);

        var search = new MissionSearch { RequireIls = true };
        var first = await svc.SearchMarketAsync(search, rng: new Random(4));

        Assert.True(first.Created > 0);
        Assert.False(first.IlsUnavailable);
        Assert.True(first.IlsChecked >= 3);
        var offers = await svc.GetContractsAsync(ContractStatus.Offered);
        Assert.All(offers, o => Assert.Contains(o.Destination, new[] { "EDDM", "EDDN" }));
        Assert.True((await svc.GetAirportsAsync(["EDDM"]))["EDDM"].HasIls);

        // Second search uses the cache: no airport is asked twice.
        int calls = lookup.Asked.Count;
        await svc.SearchMarketAsync(search, rng: new Random(5));
        Assert.Equal(lookup.Asked.Count, lookup.Asked.Distinct().Count());
        Assert.True(lookup.Asked.Count - calls <= 3);
    }

    [Fact]
    public async Task Ils_search_without_sim_reports_it_and_uses_only_known_airports()
    {
        var svc = new CareerService(_factory, new ContentStore(ContentLoader.Load(FindContentRoot())),
            new MissionGeneratorRegistry([new StandardMissionGenerator()]), new FakeIlsLookup([]) { Available = false });
        await svc.CreateCareerAsync("Test", "Test Air", "EDDF", Difficulty.Normal, null, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));
        await svc.AcquireAircraftAsync("c208b", Ownership.Rented);

        var result = await svc.SearchMarketAsync(new MissionSearch { RequireIls = true }, rng: new Random(1));

        Assert.True(result.IlsUnavailable);
        Assert.Equal(0, result.Created);
    }

    [Fact]
    public async Task Search_criteria_limit_new_offers_and_the_market_is_capped()
    {
        await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);

        var search = new MissionSearch { MissionTypeIds = new HashSet<string> { "charter" }, DistanceMinNm = 100, ScheduledServiceOnly = true };
        var r = await _svc.SearchMarketAsync(search, rng: new Random(8));

        Assert.True(r.Created > 0);
        var offers = await _svc.GetContractsAsync(ContractStatus.Offered);
        Assert.All(offers, o =>
        {
            Assert.Equal("charter", o.MissionTypeId);
            Assert.True(o.DistanceNm >= 100);
            Assert.Contains(o.Destination, new[] { "EDDM", "EDDS", "EDDN", "EDDB" });
        });

        for (int i = 0; i < 6; i++) await _svc.SearchMarketAsync(MissionSearch.Any with { }, rng: new Random(20 + i));
        Assert.True((await _svc.GetContractsAsync(ContractStatus.Offered)).Count <= CareerService.MaxOffersPerAircraft);
    }

    [Fact]
    public async Task Scheduled_search_keeps_the_large_airport_share_and_reuses_hubs()
    {
        await NewCareer(Difficulty.Easy);
        await _svc.AcquireAircraftAsync("a20n", Ownership.Rented);

        await _svc.SearchMarketAsync(new MissionSearch { MissionTypeIds = new HashSet<string> { "scheduled_pax" } }, rng: new Random(6));

        var offers = await _svc.GetContractsAsync(ContractStatus.Offered);
        Assert.Equal(CareerService.OffersPerSearch, offers.Count);
        // Airline jobs need 150 nm+ and a large/medium airport; in the test data only EDDM and EDDB qualify, so the
        // offers must share these hubs.
        var airports = await _svc.GetAirportsAsync(offers.Select(o => o.Destination));
        Assert.All(offers, o => Assert.Equal(AirportSize.Large, airports[o.Destination].Size));
        Assert.True(offers.Select(o => o.Destination).Distinct().Count() < offers.Count);
    }

    [Fact]
    public async Task Offers_use_the_career_passenger_and_bag_weight()
    {
        await NewCareer(Difficulty.Easy);
        var career = await _svc.GetCareerAsync();
        Assert.Equal(79.38, career.PassengerWeightKg);   // SimBrief's 175 lb
        Assert.Equal(22.68, career.BagWeightKg);         // and 50 lb per bag
        await _svc.AcquireAircraftAsync("a20n", Ownership.Rented);
        await Assert.ThrowsAsync<CareerException>(() => _svc.UpdateLoadWeightsAsync(10, 20));
        await Assert.ThrowsAsync<CareerException>(() => _svc.UpdateLoadWeightsAsync(80, 60));
        await _svc.UpdateLoadWeightsAsync(90, 20);

        await _svc.SearchMarketAsync(new MissionSearch { MissionTypeIds = new HashSet<string> { "scheduled_pax" } }, rng: new Random(3));

        var withPax = (await _svc.GetContractsAsync(ContractStatus.Offered)).Where(o => o.Adults > 0).ToList();
        Assert.NotEmpty(withPax);
        // Like SimBrief: children weigh as adults, every seated passenger has one bag; infants keep their lap mass.
        Assert.All(withPax, o =>
        {
            Assert.Equal(90, (o.PassengerMassKg - o.Infants * PassengerMassTable.Default.InfantKg) / (o.Adults + o.Children), 1);
            Assert.Equal(20, o.BaggageKg / (o.Adults + o.Children), 1);
        });
    }

    [Fact]
    public async Task Charter_search_keeps_the_capital_share()
    {
        await NewCareer(Difficulty.Easy);
        await _svc.AcquireAircraftAsync("a20n", Ownership.Rented);

        await _svc.SearchMarketAsync(new MissionSearch { MissionTypeIds = new HashSet<string> { "charter" } }, rng: new Random(8));

        var offers = await _svc.GetContractsAsync(ContractStatus.Offered);
        Assert.NotEmpty(offers);
        Assert.True((await _svc.FindAirportAsync("EDDB"))!.IsCapital);
        double share = _svc.MissionType("charter").Destinations.MinCapitalShare;
        Assert.True(share > 0);
        Assert.True(offers.Count(o => o.Destination == "EDDB") >= Math.Floor(share * offers.Count),
            $"{offers.Count(o => o.Destination == "EDDB")} of {offers.Count} to Berlin");
    }

    [Fact]
    public async Task Ferry_flight_books_costs_and_moves_the_aircraft()
    {
        await NewCareer();
        var plane = await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.RefreshMarketAsync(new Random(1));
        Assert.NotEmpty(await _svc.GetContractsAsync(ContractStatus.Offered));
        decimal cashBefore = (await _svc.GetCareerAsync()).Cash;

        var estimate = await _svc.EstimateRelocationAsync(plane.Id, "eddm");
        Assert.InRange(estimate.DistanceNm, 150, 175);
        Assert.Contains(estimate.Costs, c => c.Category == LedgerCategory.Rent);
        Assert.Contains(estimate.Costs, c => c.Category == LedgerCategory.Fuel);

        var done = await _svc.RelocateAircraftAsync(plane.Id, "eddm", ferryFlight: true);
        var career = await _svc.GetCareerAsync();
        Assert.Equal(cashBefore - done!.Total, career.Cash);
        var moved = (await _svc.GetFleetAsync()).Single();
        Assert.Equal("EDDM", moved.Location);
        Assert.False(moved.IsAvailable(career.GameTime));
        Assert.Equal("Überführung nach EDDM", moved.UnavailableReason);
        Assert.Equal(1, moved.Cycles);
        Assert.Empty(await _svc.GetContractsAsync(ContractStatus.Offered));
    }

    [Fact]
    public async Task Free_relocation_and_its_limits()
    {
        await NewCareer();
        var plane = await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        decimal cash = (await _svc.GetCareerAsync()).Cash;

        Assert.Null(await _svc.RelocateAircraftAsync(plane.Id, "EDDS", ferryFlight: false));
        Assert.Equal("EDDS", (await _svc.GetFleetAsync()).Single().Location);
        Assert.Equal(cash, (await _svc.GetCareerAsync()).Cash);

        await Assert.ThrowsAsync<CareerException>(() => _svc.RelocateAircraftAsync(plane.Id, "EDDS", false));   // already there
        await Assert.ThrowsAsync<CareerException>(() => _svc.RelocateAircraftAsync(plane.Id, "ETAR", false));   // military field
        await Assert.ThrowsAsync<CareerException>(() => _svc.RelocateAircraftAsync(plane.Id, "XXXX", false));   // unknown

        await _svc.RefreshMarketAsync(new Random(2));
        var offer = (await _svc.GetContractsAsync(ContractStatus.Offered)).First();
        await _svc.AcceptAsync(offer.Id);
        await Assert.ThrowsAsync<CareerException>(() => _svc.RelocateAircraftAsync(plane.Id, "EDDF", false)); // accepted job waits
    }

    [Fact]
    public async Task Red_departure_checks_cost_ten_percent_each()
    {
        await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.RefreshMarketAsync(new Random(3));
        var job = (await _svc.GetContractsAsync(ContractStatus.Offered)).OrderByDescending(o => o.Payout).First();
        var dest = (await _svc.FindAirportAsync(job.Destination))!;
        await _svc.AcceptAsync(job.Id);
        await _svc.StartFlightAsync(job.Id);

        var s = await _svc.CompleteFlightAsync(job.Id, new FlightResult
        {
            BlockTime = TimeSpan.FromMinutes(20), AirTime = TimeSpan.FromMinutes(10), FuelUsedKg = 30, TouchdownFpm = -150,
            FinalPosition = dest.Position, Parked = true, DepartureIssues = ["Zuladung (Sim 900 kg, Soll 700 kg)", "Treibstoff (zu wenig)"]
        });

        var penalties = s.Lines.Where(l => l.Description.StartsWith("Abweichung beim Abflug")).ToList();
        Assert.Equal(2, penalties.Count);
        Assert.All(penalties, l => Assert.Equal(-Math.Round(job.Payout * 0.1m, 0), l.Amount));
        Assert.Contains(s.Notes, n => n.Contains("Zuladung"));
    }

    /// <summary>Stands in for the app's flight monitor: feeds telemetry into a real <see cref="FlightSession"/>.</summary>
    private sealed class FakeHost : IFlightSessionHost
    {
        private FlightSession? _session;
        public bool Connected => true;
        public Telemetry? Latest { get; private set; }
        public FlightPhase? Phase => _session?.Phases.Phase;
        public Guid? ContractId { get; private set; }
        public bool HasSession => _session is not null;
        public IReadOnlyList<string> DepartureIssues { get; set; } = [];
        public List<string> Log { get; } = [];
        public void BeginFlight(Guid? contractId = null) { _session = new FlightSession(); ContractId = contractId; }
        public void EndFlight() { _session = null; ContractId = null; }
        public FlightSummary? Summarize() => _session?.Summarize();
        public Task<string?> SaveTrackAsync(string name) => Task.FromResult<string?>(null);
        void IFlightSessionHost.Log(string message) => Log.Add(message);

        public void Feed(Telemetry t)
        {
            Latest = t;
            _session?.Process(t);
        }
    }

    /// <summary>1 Hz telemetry: hold a state, optionally moving in a straight line to a target position.</summary>
    private static IEnumerable<Telemetry> Segment(Telemetry from, int seconds, Func<Telemetry, Telemetry> change, GeoPosition? to = null)
    {
        var s = change(from);
        var start = s.Position;
        for (int i = 1; i <= seconds; i++)
        {
            var pos = to is { } target
                ? new GeoPosition(start.Latitude + (target.Latitude - start.Latitude) * i / seconds, start.Longitude + (target.Longitude - start.Longitude) * i / seconds)
                : s.Position;
            s = s with { Timestamp = s.Timestamp.AddSeconds(1), Latitude = pos.Latitude, Longitude = pos.Longitude };
            yield return s;
        }
    }

    private async Task<(FlightAutomation Automation, FakeHost Host, MissionContract Job, Telemetry Parked)> ArmedCaravanJobAsync(string title)
    {
        await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.SearchMarketAsync(new MissionSearch { MissionTypeIds = new HashSet<string> { "cargo_feeder" } }, rng: new Random(4));
        var job = (await _svc.GetContractsAsync(ContractStatus.Offered)).OrderBy(o => o.DistanceNm).First();
        await _svc.AcceptAsync(job.Id);
        job = (await _svc.GetContractsAsync(ContractStatus.Accepted)).Single();

        var sheet = _svc.BuildLoadSheet(job, _svc.Profile("c208b"));
        var eddf = (await _svc.FindAirportAsync("EDDF"))!;
        var parked = new Telemetry
        {
            Timestamp = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero), Latitude = eddf.Position.Latitude, Longitude = eddf.Position.Longitude,
            OnGround = true, ParkingBrake = true, AircraftTitle = title, PayloadKg = sheet.SimPayload.Kg, FuelKg = sheet.Fuel.Block.Kg,
            TotalWeightKg = 2145 + sheet.SimPayload.Kg + sheet.Fuel.Block.Kg
        };
        var host = new FakeHost();
        var automation = new FlightAutomation(_svc, host);
        host.Feed(parked);
        await automation.TickAsync();
        return (automation, host, job, parked);
    }

    [Fact]
    public async Task Flight_is_armed_on_the_ground_started_at_liftoff_and_settled_after_parking()
    {
        var (automation, host, job, parked) = await ArmedCaravanJobAsync("Cessna 208B Grand Caravan EX");
        Assert.Equal(job.Id, host.ContractId);
        // Only the optional SimBrief OFP is missing.
        Assert.Equal(Light.Yellow, automation.Readiness[job.Id].Overall);
        Assert.Equal(DepartureCheck.Ofp, Assert.Single(automation.Readiness[job.Id].Items, i => i.Light != Light.Green).Key);

        async Task Fly(IEnumerable<Telemetry> samples)
        {
            foreach (var t in samples) host.Feed(t);
            await automation.TickAsync();
        }

        var dest = (await _svc.FindAirportAsync(job.Destination))!;
        var t0 = parked with { AnyEngineRunning = true };
        var taxi = Segment(t0, 30, t => t with { ParkingBrake = false, GroundSpeedKt = 12 }).ToList();
        await Fly(taxi);
        Assert.Equal(FlightPhase.TaxiOut, host.Phase);
        Assert.Equal(ContractStatus.Accepted, (await _svc.GetContractsAsync(ContractStatus.Accepted)).Single().Status);   // still editable

        var roll = Segment(taxi[^1], 10, t => t with { GroundSpeedKt = 70 }).ToList();
        await Fly(roll);
        var liftoff = Segment(roll[^1], 10, t => t with { OnGround = false, GroundSpeedKt = 100, VerticalSpeedFpm = 800, AltitudeAglFt = 500 }).ToList();
        await Fly(liftoff);
        Assert.Equal(ContractStatus.InProgress, (await _svc.GetContractsAsync(ContractStatus.InProgress)).Single().Status);

        // Straight to the destination at a pace the teleport check accepts, then land, taxi and park.
        double nm = liftoff[^1].Position.DistanceNmTo(dest.Position);
        int seconds = (int)Math.Ceiling(nm / 300.0 * 3600);
        var cruise = Segment(liftoff[^1], seconds, t => t with { GroundSpeedKt = 300, VerticalSpeedFpm = 0, AltitudeAglFt = 5000 }, dest.Position).ToList();
        var landing = Segment(cruise[^1], 1, t => t with { OnGround = true, AltitudeAglFt = 0, VerticalSpeedFpm = 0, TouchdownVsFpm = -150, GForce = 1.2, GroundSpeedKt = 60 })
            .Concat(Segment(cruise[^1] with { OnGround = true, AltitudeAglFt = 0 }, 20, t => t with { GForce = 1, GroundSpeedKt = 10, TouchdownVsFpm = 0 }))
            .ToList();
        var park = Segment(landing[^1], 10, t => t with { GroundSpeedKt = 0, ParkingBrake = true, AnyEngineRunning = false }).ToList();
        await Fly(cruise.Concat(landing).Concat(park));

        Assert.Equal(FlightPhase.Arrived, host.Phase is null ? FlightPhase.Arrived : host.Phase);
        Assert.NotNull(automation.LastSettlement);
        Assert.Contains(automation.LastSettlement!.Outcome, new[] { FlightOutcome.Completed, FlightOutcome.Late });
        Assert.False(host.HasSession);
        Assert.Single(await _svc.GetContractsAsync(ContractStatus.Completed));
        Assert.Equal(job.Destination, (await _svc.GetFleetAsync()).Single().Location);
    }

    [Fact]
    public async Task Liftoff_in_the_wrong_aircraft_does_not_start_the_job()
    {
        var (automation, host, job, parked) = await ArmedCaravanJobAsync("Airbus A320neo");
        Assert.Equal(job.Id, host.ContractId);   // armed anyway, so the light can show what is wrong
        Assert.Equal(Light.Red, automation.Readiness[job.Id][DepartureCheck.Type]!.Light);

        var samples = Segment(parked with { AnyEngineRunning = true }, 20, t => t with { ParkingBrake = false, GroundSpeedKt = 12 }).ToList();
        samples.AddRange(Segment(samples[^1], 10, t => t with { GroundSpeedKt = 70 }));
        samples.AddRange(Segment(samples[^1], 10, t => t with { OnGround = false, GroundSpeedKt = 100, VerticalSpeedFpm = 800, AltitudeAglFt = 500 }));
        foreach (var t in samples) host.Feed(t);
        await automation.TickAsync();

        Assert.False(host.HasSession);
        Assert.Contains("Flugzeugtyp", automation.NotStarted[job.Id]);
        Assert.Single(await _svc.GetContractsAsync(ContractStatus.Accepted));
    }

    [Fact]
    public async Task Armed_job_is_dropped_when_the_aircraft_appears_elsewhere()
    {
        var (automation, host, _, parked) = await ArmedCaravanJobAsync("Cessna 208B Grand Caravan EX");
        Assert.True(host.HasSession);
        var munich = (await _svc.FindAirportAsync("EDDM"))!;
        host.Feed(parked with { Timestamp = parked.Timestamp.AddSeconds(1), Latitude = munich.Position.Latitude, Longitude = munich.Position.Longitude });
        await automation.TickAsync();
        Assert.False(host.HasSession);
    }

    private sealed class FakeIlsLookup(Dictionary<string, bool> known) : IAirportIlsLookup
    {
        public bool Available { get; init; } = true;
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyDictionary<string, AirportIlsInfo?>> CheckAsync(IReadOnlyList<string> idents, CancellationToken ct = default)
        {
            Asked.AddRange(idents);
            IReadOnlyDictionary<string, AirportIlsInfo?> result = idents.ToDictionary(i => i,
                i => known.TryGetValue(i, out var ils) ? new AirportIlsInfo(ils, ils ? "25:IXX/07:IYY" : "") : (AirportIlsInfo?)null);
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task New_career_gets_starting_capital()
    {
        var career = await NewCareer();
        Assert.Equal(350_000m, career.Cash);
        Assert.Single(await _svc.GetLedgerAsync());
        await Assert.ThrowsAsync<CareerException>(() => _svc.CreateCareerAsync("X", "Y", "ZZZZ", Difficulty.Normal, null));
    }

    [Fact]
    public async Task Buying_requires_enough_cash_and_leasing_charges_pro_rata()
    {
        await NewCareer();
        await Assert.ThrowsAsync<CareerException>(() => _svc.AcquireAircraftAsync("a20n", Ownership.Owned));

        var leased = await _svc.AcquireAircraftAsync("c208b", Ownership.Leased);
        var career = await _svc.GetCareerAsync();
        // 15 Oct 08:00 → 400 of 744 hours remain: 28,000 € × 0.5376 ≈ 15,054 €.
        Assert.Equal(350_000m - 15_054m, career.Cash);
        Assert.Equal("D-SCAA", leased.Registration);
        Assert.Equal("EDDF", leased.Location);
    }

    [Fact]
    public async Task Full_flight_cycle_pays_out_and_moves_the_aircraft()
    {
        await NewCareer();
        var aircraft = await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        Assert.True(await _svc.RefreshMarketAsync(new Random(3)) > 0);

        var offers = await _svc.GetContractsAsync(ContractStatus.Offered);
        Assert.All(offers, o => Assert.Equal("EDDF", o.Origin));
        var job = offers.OrderByDescending(o => o.Payout).First();
        var dest = (await _svc.FindAirportAsync(job.Destination))!;

        await _svc.AcceptAsync(job.Id);
        await _svc.StartFlightAsync(job.Id);
        var before = await _svc.GetCareerAsync();

        var result = new FlightResult
        {
            BlockTime = TimeSpan.FromHours(job.DistanceNm / 175 + 0.3),
            AirTime = TimeSpan.FromHours(job.DistanceNm / 175),
            DistanceFlownNm = job.DistanceNm,
            FuelUsedKg = 175 * job.DistanceNm / 175,
            TouchdownFpm = -150,
            MaxGForce = 1.2,
            FinalPosition = dest.Position,
            Parked = true
        };
        var s = await _svc.CompleteFlightAsync(job.Id, result);

        Assert.Contains(s.Outcome, new[] { FlightOutcome.Completed, FlightOutcome.Late });
        Assert.True(s.Revenue >= job.Payout * 0.75m);
        Assert.Contains(s.Lines, l => l.Category == LedgerCategory.Rent);
        Assert.DoesNotContain(s.Lines, l => l.Category == LedgerCategory.Maintenance);

        var after = await _svc.GetCareerAsync();
        Assert.Equal(before.Cash + s.Profit, after.Cash);
        Assert.True(after.GameTime > before.GameTime);

        var moved = (await _svc.GetFleetAsync()).Single(f => f.Id == aircraft.Id);
        Assert.Equal(job.Destination, moved.Location);
        Assert.Equal(1, moved.Cycles);
        Assert.Single(await _svc.GetFlightsAsync());
        Assert.Empty(await _svc.GetContractsAsync(ContractStatus.InProgress));
    }

    [Fact]
    public async Task Landing_elsewhere_is_a_diversion_without_pay()
    {
        await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.RefreshMarketAsync(new Random(5));
        var job = (await _svc.GetContractsAsync(ContractStatus.Offered)).First(o => o.Destination != "EDFE");
        await _svc.AcceptAsync(job.Id);
        await _svc.StartFlightAsync(job.Id);

        var egelsbach = (await _svc.FindAirportAsync("EDFE"))!;
        var s = await _svc.CompleteFlightAsync(job.Id, new FlightResult
        {
            BlockTime = TimeSpan.FromMinutes(40), AirTime = TimeSpan.FromMinutes(25), FuelUsedKg = 80,
            FinalPosition = egelsbach.Position, TouchdownFpm = -200
        });

        Assert.Equal(FlightOutcome.Diverted, s.Outcome);
        Assert.Equal("EDFE", s.FinalLocation);
        Assert.DoesNotContain(s.Lines, l => l.Category == LedgerCategory.MissionRevenue);
        Assert.Equal("EDFE", (await _svc.GetFleetAsync()).Single().Location);
    }

    [Fact]
    public async Task Severe_violation_voids_the_job()
    {
        await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.RefreshMarketAsync(new Random(9));
        var job = (await _svc.GetContractsAsync(ContractStatus.Offered)).First();
        await _svc.AcceptAsync(job.Id);
        await _svc.StartFlightAsync(job.Id);
        var dest = (await _svc.FindAirportAsync(job.Destination))!;

        var s = await _svc.CompleteFlightAsync(job.Id, new FlightResult
        {
            AirTime = TimeSpan.FromMinutes(30), BlockTime = TimeSpan.FromMinutes(45), FuelUsedKg = 90,
            FinalPosition = dest.Position, SevereViolations = 1
        });

        Assert.Equal(FlightOutcome.Failed, s.Outcome);
        Assert.Equal(0, s.Score.Total);
        Assert.Equal(ContractStatus.Failed, (await _svc.GetContractsAsync(ContractStatus.Failed)).Single().Status);
        Assert.True((await _svc.GetCareerAsync()).Reputation < 10);
    }

    [Fact]
    public async Task Waiting_books_monthly_lease_and_insurance()
    {
        await NewCareer();
        var leased = await _svc.AcquireAircraftAsync("c208b", Ownership.Leased);
        await _svc.WaitAsync(TimeSpan.FromDays(50)); // 15 Oct → 4 Dec: Nov 1 and Dec 1 boundaries

        var ledger = await _svc.GetLedgerAsync();
        Assert.Equal(2, ledger.Count(l => l.Category == LedgerCategory.Lease && l.Description.StartsWith("Leasingrate")));
        Assert.Equal(2, ledger.Count(l => l.Category == LedgerCategory.Insurance));
        Assert.All(ledger.Where(l => l.Category == LedgerCategory.Lease && l.Description.StartsWith("Leasingrate")),
            l => Assert.Equal(-leased.MonthlyRate, l.Amount));

        var career = await _svc.GetCareerAsync();
        Assert.Equal(ledger.Sum(l => l.Amount), career.Cash);
    }

    [Fact]
    public async Task Selling_returns_dealer_value_and_cancel_costs_ten_percent()
    {
        await NewCareer(Difficulty.Easy); // a new C172 (450,000 €) exceeds the Normal starting capital
        var plane = await _svc.AcquireAircraftAsync("c172", Ownership.Owned);
        await _svc.RefreshMarketAsync(new Random(1));
        var job = (await _svc.GetContractsAsync(ContractStatus.Offered)).First();
        await _svc.AcceptAsync(job.Id);
        await Assert.ThrowsAsync<CareerException>(() => _svc.DisposeAircraftAsync(plane.Id));

        var cashBefore = (await _svc.GetCareerAsync()).Cash;
        await _svc.CancelAsync(job.Id);
        Assert.Equal(cashBefore - Math.Round(job.Payout * 0.1m, 0), (await _svc.GetCareerAsync()).Cash);

        var proceeds = await _svc.DisposeAircraftAsync(plane.Id);
        Assert.Equal(Math.Round(450_000m * 0.9m, 0), proceeds);
        Assert.Empty(await _svc.GetFleetAsync());
    }

    [Fact]
    public async Task Deleting_a_career_removes_its_data_and_selection()
    {
        var career = await NewCareer();
        await _svc.AcquireAircraftAsync("c208b", Ownership.Rented);
        await _svc.RefreshMarketAsync(new Random(2));

        await _svc.DeleteCareerAsync(career.Id);

        Assert.Null(_svc.ActiveCareerId);
        Assert.Empty(await _svc.ListCareersAsync());
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Fleet);
        Assert.Empty(db.Contracts);
        Assert.Empty(db.Ledger);
        Assert.Equal(8, db.Airports.Count());
    }

    private sealed class TestDbFactory(DbContextOptions<SkyCareerDb> options) : IDbContextFactory<SkyCareerDb>
    {
        public SkyCareerDb CreateDbContext() => new(options);
    }
}
