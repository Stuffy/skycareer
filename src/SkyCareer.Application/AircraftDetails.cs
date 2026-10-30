using SkyCareer.Domain.Career;

namespace SkyCareer.Application;

/// <summary>One fleet aircraft with its flights, ledger entries and open jobs (newest first, jobs by deadline).</summary>
public sealed record AircraftDetails(FleetAircraft Aircraft, IReadOnlyList<FlightRecord> Flights, IReadOnlyList<LedgerEntry> Ledger,
    IReadOnlyList<MissionContract> OpenJobs);
