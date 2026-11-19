namespace Caravan.Spi;

/// <summary>
/// Tracks the exchanges being routed (Camel's <c>InflightRepository</c>). Fed by every route: an exchange is
/// inflight from when a route receives it until that route is done with it.
/// </summary>
public interface IInflightRepository
{
    /// <summary>The number of distinct exchanges inflight.</summary>
    int Size { get; }

    /// <summary>The number of exchanges inflight in a route.</summary>
    int GetSize(string routeId);

    /// <summary>Records that <paramref name="routeId"/> started processing <paramref name="exchange"/>.</summary>
    void Add(Exchange exchange, string routeId);

    /// <summary>Records that <paramref name="routeId"/> finished processing <paramref name="exchange"/>.</summary>
    void Remove(Exchange exchange, string routeId);

    /// <summary>Lists the inflight exchanges.</summary>
    /// <param name="routeId">Only exchanges currently inside this route, when given.</param>
    /// <param name="limit">The maximum number of entries; negative for all.</param>
    /// <param name="sortByLongestDuration">Sort with the longest-running exchange first.</param>
    IReadOnlyList<InflightExchangeInfo> Browse(string? routeId = null, int limit = -1, bool sortByLongestDuration = false);
}
