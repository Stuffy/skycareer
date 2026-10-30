using SkyCareer.Domain.Aircraft;
using SkyCareer.Integrations.SimBrief;

namespace SkyCareer.Missions.Tests;

public class SimBriefCatalogTests
{
    // Trimmed copy of SimBrief's inputs.airframes.json; the user airframes are synthetic placeholders.
    private const string Fixture = """
        {"C750":{"aircraft_id":"C750","aircraft_icao":"C750","aircraft_name":"Citation X","aircraft_engines":"AE3007C1","aircraft_search":"cessna 750 citation 10","aircraft_passengers":12,"aircraft_mtow_kgs":16375,"aircraft_speed":"Mach 0.86","aircraft_ceiling":51000,"aircraft_takeoff":5140,"aircraft_landing":3400,"aircraft_fuelflow_kgs":807,"aircraft_is_cargo":false,"airframes":[{"airframe_id":false,"airframe_options":{"maxpax":12,"wgtunits":"LBS","oew":22131,"mzfw":24400,"mtow":36100,"mlw":31800,"maxfuel":13000,"paxwgt":175,"bagwgt":55}},{"airframe_id":1700000000000,"user_id":"00000000-test","pilot_id":1,"airframe_options":{"maxpax":1,"wgtunits":"LBS","oew":1,"mzfw":1,"mtow":1,"mlw":1,"maxfuel":1,"paxwgt":1,"bagwgt":1}}]},"C172":{"aircraft_id":"C172","aircraft_icao":"C172","aircraft_name":"Cessna 172R","aircraft_engines":"IO-360-L2A","aircraft_search":"aviones colombia 172 cessna cutlass skyhawk p172 powermatic r172 hawk xp t-41 mescalero","aircraft_passengers":3,"aircraft_mtow_kgs":1111,"aircraft_speed":"105 KIAS","aircraft_ceiling":12000,"aircraft_takeoff":1600,"aircraft_landing":1250,"aircraft_fuelflow_kgs":20,"aircraft_is_cargo":false,"airframes":[{"airframe_id":false,"airframe_options":{"maxpax":3,"wgtunits":"LBS","oew":1631,"mzfw":2450,"mtow":2450,"mlw":2450,"maxfuel":336,"paxwgt":175,"bagwgt":55}},{"airframe_id":1700000000000,"user_id":"00000000-test","pilot_id":1,"airframe_options":{"maxpax":1,"wgtunits":"LBS","oew":1,"mzfw":1,"mtow":1,"mlw":1,"maxfuel":1,"paxwgt":1,"bagwgt":1}}]},"DH8D":{"aircraft_id":"DH8D","aircraft_icao":"DH8D","aircraft_name":"DHC8-402","aircraft_engines":"PW150A","aircraft_search":"bombardier dhc-8-400 dash 8 de havilland canada","aircraft_passengers":74,"aircraft_mtow_kgs":29574,"aircraft_speed":"280 KIAS","aircraft_ceiling":25000,"aircraft_takeoff":6000,"aircraft_landing":2747,"aircraft_fuelflow_kgs":844,"aircraft_is_cargo":false,"airframes":[{"airframe_id":false,"airframe_options":{"maxpax":74,"wgtunits":"LBS","oew":39111,"mzfw":58000,"mtow":65200,"mlw":62000,"maxfuel":11724,"paxwgt":175,"bagwgt":55}},{"airframe_id":1700000000000,"user_id":"00000000-test","pilot_id":1,"airframe_options":{"maxpax":1,"wgtunits":"LBS","oew":1,"mzfw":1,"mtow":1,"mlw":1,"maxfuel":1,"paxwgt":1,"bagwgt":1}}]},"C408":{"aircraft_id":"C408","aircraft_icao":"C408","aircraft_name":"Cessna 408","aircraft_engines":"PT6A-65SC","aircraft_search":"cessna 408 skycourier","aircraft_passengers":19,"aircraft_mtow_kgs":8618,"aircraft_speed":"187 KIAS","aircraft_ceiling":20000,"aircraft_takeoff":false,"aircraft_landing":false,"aircraft_fuelflow_kgs":363,"aircraft_is_cargo":false,"airframes":[{"airframe_id":1700000000000,"user_id":"00000000-test","pilot_id":1,"airframe_options":{"maxpax":1,"wgtunits":"LBS","oew":1,"mzfw":1,"mtow":1,"mlw":1,"maxfuel":1,"paxwgt":1,"bagwgt":1}}]}}
        """;

    private static readonly SimBriefAircraftCatalog Catalog = SimBriefAircraftCatalog.Parse(Fixture);
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    [Fact]
    public void Catalog_reads_default_airframes_in_kg_and_ignores_user_airframes()
    {
        Assert.Equal(4, Catalog.Types.Count);
        var c750 = Catalog.Find("c750")!;
        Assert.Equal(10038, c750.OperatingEmptyKg);
        Assert.Equal(11068, c750.MaxZeroFuelKg);
        Assert.Equal(16375, c750.MaxTakeoffKg);
        Assert.Equal(14424, c750.MaxLandingKg);
        Assert.Equal(5897, c750.MaxFuelKg);
        Assert.Equal(12, c750.MaxPassengers);
        Assert.Equal(79.38, c750.PassengerWeightKg);
        Assert.Equal(493, c750.CruiseKtas);           // Mach 0.86 × 573.6 kt
        Assert.True(c750.CruisesAtMach);

        Assert.Equal(123, Catalog.Find("C172")!.CruiseKtas);  // 105 KIAS at 8 400 ft
        // No default airframe: only the type-level MTOW and seats, never the user's airframe.
        var c408 = Catalog.Find("C408")!;
        Assert.Equal(8618, c408.MaxTakeoffKg);
        Assert.Equal(19, c408.MaxPassengers);
        Assert.Equal(0, c408.OperatingEmptyKg);
    }

    [Fact]
    public void Catalog_search_matches_icao_name_and_search_words()
    {
        Assert.Equal("C172", Catalog.Search("skyhawk").Single().Id);
        Assert.Equal("DH8D", Catalog.Search("dash 8").Single().Id);
        Assert.Equal("C750", Catalog.Search("c750").First().Id);
        Assert.Equal(4, Catalog.Search("").Count());
    }

    [Fact]
    public void Profile_from_simbrief_alone_is_complete_without_stations()
    {
        var p = SimBriefProfileDraft.FromSimBrief(Catalog.Find("C172")!, Content.Aircraft);
        Assert.Equal("C172", p.IcaoType);
        Assert.Equal("C172", p.SimBriefAirframe);
        Assert.Equal(FuelType.AvGas, p.FuelType);
        Assert.Contains("piston", p.Tags);
        Assert.Equal(LoadingMethod.Manual, p.Loading);
        Assert.Empty(p.Stations);
        Assert.Equal(488, p.MinRunwayM);
        Assert.True(p.Economics.RentPerBlockHour > 0);     // estimated from a similar profile
        Assert.True(AircraftProfileValidator.IsValid(p), string.Join("; ", AircraftProfileValidator.Validate(p).Select(i => i.Message)));

        var c408 = SimBriefProfileDraft.FromSimBrief(Catalog.Find("C408")!, Content.Aircraft);
        Assert.Equal(FuelType.JetA, c408.FuelType);
        Assert.Contains("turboprop", c408.Tags);
        Assert.True(c408.MaxLandingKg > 0 && c408.MaxLandingKg <= c408.MaxTakeoffKg);
    }

    [Fact]
    public void Merge_keeps_sim_weight_and_stations_and_warns_about_a_different_oew()
    {
        var simDraft = new AircraftProfile
        {
            Id = "citation_x_winglets", Name = "Citation X Winglets", IcaoType = "ZZZZ", SimMatch = ["Citation X Winglets"],
            OperatingEmptyKg = 11000, MaxZeroFuelKg = 11100, MaxTakeoffKg = 16375, MaxLandingKg = 14700, MaxFuelKg = 5800,
            PassengerSeats = 9, Stations = [new PayloadStation(1, "Pilot", StationKind.Crew, 1)]
        };
        var (merged, warning) = SimBriefProfileDraft.Merge(simDraft, Catalog.Find("C750")!);
        Assert.Equal(11000, merged.OperatingEmptyKg);
        Assert.Equal(11068, merged.MaxZeroFuelKg);
        Assert.Equal(14424, merged.MaxLandingKg);
        Assert.Equal(9, merged.PassengerSeats);
        Assert.Single(merged.Stations);
        Assert.Equal("C750", merged.IcaoType);
        Assert.Contains("C750", merged.SimMatch);
        Assert.Contains("Citation X Winglets", merged.SimMatch);
        Assert.NotNull(warning);

        Assert.Null(SimBriefProfileDraft.Merge(simDraft with { OperatingEmptyKg = 10200 }, Catalog.Find("C750")!).Warning);
    }
}
