namespace SkyCareer.Application;

/// <summary>ILS facts about one airport as reported by the simulator.</summary>
public sealed record AirportIlsInfo(bool HasIls, string IlsRunways);

/// <summary>
/// Asks the simulator which airports have an ILS. Implemented by the app on top of SimConnect; when the sim isn't
/// running <see cref="Available"/> is false and nothing is returned.
/// </summary>
public interface IAirportIlsLookup
{
    bool Available { get; }

    /// <summary>
    /// Checks the given airports. The result contains an entry for every airport that was checked; the value is null
    /// when the sim doesn't know the airport. Airports missing from the result (timeout, cancellation) stay unknown.
    /// </summary>
    Task<IReadOnlyDictionary<string, AirportIlsInfo?>> CheckAsync(IReadOnlyList<string> idents, CancellationToken ct = default);
}

/// <summary>Result of a market search.</summary>
/// <param name="Created">New offers added to the market.</param>
/// <param name="IlsChecked">Airports newly checked for ILS in the simulator during this search.</param>
/// <param name="IlsUnavailable">ILS was required but the simulator wasn't available, so only already known ILS airports were used.</param>
public sealed record MarketSearchResult(int Created, int IlsChecked, bool IlsUnavailable);
