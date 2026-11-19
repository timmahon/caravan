namespace Caravan.Spi;

/// <summary>
/// Holds the data stored by the <c>claimCheck</c> EIP for one exchange (Camel's <c>ClaimCheckRepository</c>): copies of the exchange by key, and a
/// stack for push/pop. The repository lives in the exchange property <see cref="ExchangePropertyKeys.ClaimCheckRepository"/>
/// (<see cref="Support.MemoryClaimCheckRepository"/> by default), so it travels with the exchange and its copies.
/// </summary>
public interface IClaimCheckRepository
{
    /// <summary>Stores <paramref name="exchange"/> under <paramref name="key"/>, replacing any exchange stored under it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="exchange">The exchange (a copy made by the caller).</param>
    /// <returns>True when the key was new, false when an exchange was replaced.</returns>
    bool Add(string key, Exchange exchange);

    /// <summary>Whether an exchange is stored under <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    bool Contains(string key);

    /// <summary>The exchange stored under <paramref name="key"/>, or null; it stays stored.</summary>
    /// <param name="key">The key.</param>
    Exchange? Get(string key);

    /// <summary>Removes and returns the exchange stored under <paramref name="key"/>, or null.</summary>
    /// <param name="key">The key.</param>
    Exchange? GetAndRemove(string key);

    /// <summary>Pushes <paramref name="exchange"/> on the stack.</summary>
    /// <param name="exchange">The exchange (a copy made by the caller).</param>
    void Push(Exchange exchange);

    /// <summary>Pops the latest pushed exchange, or null when the stack is empty.</summary>
    Exchange? Pop();

    /// <summary>Removes everything.</summary>
    void Clear();
}
