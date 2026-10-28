using SkyCareer.Domain.Aircraft;

namespace SkyCareer.App.Services;

/// <summary>Editable copy of an <see cref="AircraftProfile"/> for the profile editor (records are immutable).</summary>
public sealed class ProfileForm
{
    public sealed class StationRow
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public StationKind Kind { get; set; } = StationKind.Passengers;
        public int Seats { get; set; }
        public double MaxKg { get; set; }
    }

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string IcaoType { get; set; } = "";
    public string SimMatch { get; set; } = "";
    public double OperatingEmptyKg { get; set; }
    public bool CrewIncludedInEmptyWeight { get; set; }
    public double CrewMassKg { get; set; } = 85;
    public double CabinCrewMassKg { get; set; } = 75;
    public double MaxZeroFuelKg { get; set; }
    public double MaxTakeoffKg { get; set; }
    public double MaxLandingKg { get; set; }
    public double MaxFuelKg { get; set; }
    public FuelType FuelType { get; set; } = FuelType.JetA;
    public int PassengerSeats { get; set; }
    public double MaxCargoKg { get; set; }
    public int CrewCount { get; set; } = 1;
    public int CabinCrewCount { get; set; }
    public double CruiseKtas { get; set; }
    public double FuelBurnKgPerHour { get; set; }
    public double TaxiFuelKg { get; set; }
    public string? SimBriefAirframe { get; set; }
    public double MinRunwayM { get; set; }
    public IReadOnlyCollection<RunwaySurface> Surfaces { get; set; } = [RunwaySurface.Paved];
    public IReadOnlyCollection<string> Tags { get; set; } = [];
    public string ExtraTags { get; set; } = "";
    public LoadingMethod Loading { get; set; } = LoadingMethod.SimConnectStations;
    public List<StationRow> Stations { get; set; } = [];
    public decimal PurchasePriceNew { get; set; }
    public decimal LeasePerMonth { get; set; }
    public decimal RentPerBlockHour { get; set; }
    public decimal MaintenancePerFlightHour { get; set; }
    public decimal InsurancePerMonth { get; set; }

    public static ProfileForm From(AircraftProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        IcaoType = p.IcaoType,
        SimMatch = string.Join(", ", p.SimMatch),
        OperatingEmptyKg = p.OperatingEmptyKg,
        CrewIncludedInEmptyWeight = p.CrewIncludedInEmptyWeight,
        CrewMassKg = p.CrewMassKg,
        CabinCrewMassKg = p.CabinCrewMassKg,
        MaxZeroFuelKg = p.MaxZeroFuelKg,
        MaxTakeoffKg = p.MaxTakeoffKg,
        MaxLandingKg = p.MaxLandingKg,
        MaxFuelKg = p.MaxFuelKg,
        FuelType = p.FuelType,
        PassengerSeats = p.PassengerSeats,
        MaxCargoKg = p.MaxCargoKg,
        CrewCount = p.CrewCount,
        CabinCrewCount = p.CabinCrewCount,
        CruiseKtas = p.CruiseKtas,
        FuelBurnKgPerHour = p.FuelBurnKgPerHour,
        TaxiFuelKg = p.TaxiFuelKg,
        SimBriefAirframe = p.SimBriefAirframe,
        MinRunwayM = p.MinRunwayM,
        Surfaces = p.Surfaces.ToList(),
        Tags = p.Tags.ToList(),
        Loading = p.Loading,
        Stations = p.Stations.Select(s => new StationRow { Index = s.Index, Name = s.Name, Kind = s.Kind, Seats = s.Seats, MaxKg = s.MaxKg }).ToList(),
        PurchasePriceNew = p.Economics.PurchasePriceNew,
        LeasePerMonth = p.Economics.LeasePerMonth,
        RentPerBlockHour = p.Economics.RentPerBlockHour,
        MaintenancePerFlightHour = p.Economics.MaintenancePerFlightHour,
        InsurancePerMonth = p.Economics.InsurancePerMonth
    };

    public AircraftProfile ToProfile() => new()
    {
        Id = Id.Trim(),
        Name = Name.Trim(),
        IcaoType = IcaoType.Trim().ToUpperInvariant(),
        SimMatch = SimMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        OperatingEmptyKg = OperatingEmptyKg,
        CrewIncludedInEmptyWeight = CrewIncludedInEmptyWeight,
        CrewMassKg = CrewMassKg,
        CabinCrewMassKg = CabinCrewMassKg,
        MaxZeroFuelKg = MaxZeroFuelKg,
        MaxTakeoffKg = MaxTakeoffKg,
        MaxLandingKg = MaxLandingKg,
        MaxFuelKg = MaxFuelKg,
        FuelType = FuelType,
        PassengerSeats = PassengerSeats,
        MaxCargoKg = MaxCargoKg,
        CrewCount = CrewCount,
        CabinCrewCount = CabinCrewCount,
        CruiseKtas = CruiseKtas,
        FuelBurnKgPerHour = FuelBurnKgPerHour,
        TaxiFuelKg = TaxiFuelKg,
        SimBriefAirframe = string.IsNullOrWhiteSpace(SimBriefAirframe) ? null : SimBriefAirframe.Trim(),
        MinRunwayM = MinRunwayM,
        Surfaces = Surfaces.Distinct().ToList(),
        Tags = Tags.Concat(ExtraTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(t => t.ToLowerInvariant().Replace(' ', '_')).Distinct().ToList(),
        Loading = Loading,
        Stations = Stations.OrderBy(s => s.Index)
            .Select(s => new PayloadStation(s.Index, s.Name.Trim(), s.Kind, s.Kind is StationKind.Passengers or StationKind.Crew ? s.Seats : 0,
                s.Kind is StationKind.Baggage or StationKind.Cargo ? s.MaxKg : 0))
            .ToList(),
        Economics = new AircraftEconomics
        {
            PurchasePriceNew = PurchasePriceNew,
            LeasePerMonth = LeasePerMonth,
            RentPerBlockHour = RentPerBlockHour,
            MaintenancePerFlightHour = MaintenancePerFlightHour,
            InsurancePerMonth = InsurancePerMonth
        }
    };

    /// <summary>Passenger seats as the sum of the passenger stations (handy after editing stations).</summary>
    public int SeatsFromStations => Stations.Where(s => s.Kind == StationKind.Passengers).Sum(s => s.Seats);

    /// <summary>Hold capacity as the sum of baggage and cargo stations.</summary>
    public double CargoFromStations => Stations.Where(s => s.Kind is StationKind.Baggage or StationKind.Cargo).Sum(s => s.MaxKg);
}
