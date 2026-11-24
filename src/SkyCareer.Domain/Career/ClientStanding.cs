namespace SkyCareer.Domain.Career;

/// <summary>
/// A career's relationship with one client: standing 0–100, flights flown, its active series and how far its story
/// chains got. Created on the first job for the client.
/// </summary>
public sealed class ClientStanding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerId { get; set; }
    public required string ClientId { get; set; }
    public int Standing { get; set; } = 10;
    public int FlightsFlown { get; set; }
    /// <summary>Flights asked for by the running series; null = no series.</summary>
    public int? SeriesTarget { get; set; }
    public int SeriesFlown { get; set; }
    public DateTime? SeriesEndsAt { get; set; }
    /// <summary>Payouts of the series' flights so far; the bonus is a share of them.</summary>
    public decimal SeriesPayouts { get; set; }
    /// <summary>The chain being flown and the next step (0-based) in it.</summary>
    public string? ChainId { get; set; }
    public int ChainStep { get; set; }
    /// <summary>Chains flown to the end, comma-separated.</summary>
    public string CompletedChains { get; set; } = "";

    public bool HasSeries => SeriesTarget is not null;
    public bool Completed(string chainId) => CompletedChains.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(chainId, StringComparer.OrdinalIgnoreCase);
}
