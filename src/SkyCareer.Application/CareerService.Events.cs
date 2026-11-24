using Microsoft.EntityFrameworkCore;
using SkyCareer.Domain;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Application;

public sealed partial class CareerService
{
    /// <summary>Airports within <paramref name="radiusNm"/> of a position (for diversions and new destinations).</summary>
    public async Task<List<Airport>> AirportsAroundAsync(GeoPosition position, double radiusNm)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await AirportsWithinAsync(db, position, radiusNm, 0, await LoadIlsCacheAsync(db, default));
    }

    /// <summary>
    /// Stores the crew's decision on an event during the flight. Accepted: the flight now goes to
    /// <paramref name="revisedDestination"/>, at <paramref name="newPayout"/> when given, with the deadline moved by
    /// <paramref name="extraHours"/>. Declined: only noted; the settlement judges it.
    /// </summary>
    public async Task RecordEventAsync(Guid contractId, InFlightEventKind kind, bool accepted, string? revisedDestination = null,
        decimal? newPayout = null, double extraHours = 0)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await RequireContract(db, contractId);
        if (c.Status != ContractStatus.InProgress) throw new CareerException(L.T("Only a job in flight can react to an event."));
        c.EventKind = kind.ToString();
        c.EventAccepted = accepted;
        if (accepted && revisedDestination is not null)
        {
            c.RevisedDestination = revisedDestination.ToUpperInvariant();
            if (newPayout is { } pay) c.Payout = pay;
            if (extraHours > 0) c.Deadline = c.Deadline.AddHours(extraHours);
        }
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }
}
