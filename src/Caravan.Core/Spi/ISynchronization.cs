namespace Caravan.Spi;

/// <summary>A callback invoked when an exchange completes (Camel's <c>Synchronization</c>).</summary>
public interface ISynchronization
{
    /// <summary>Called when the exchange completed successfully.</summary>
    ValueTask OnCompleteAsync(Exchange exchange);

    /// <summary>Called when the exchange failed.</summary>
    ValueTask OnFailureAsync(Exchange exchange);
}
