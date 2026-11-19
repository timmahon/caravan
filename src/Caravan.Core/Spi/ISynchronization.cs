namespace Caravan.Spi;

/// <summary>A callback invoked when an exchange completes (Camel's <c>Synchronization</c>).</summary>
public interface ISynchronization
{
    /// <summary>Called when the exchange completed successfully.</summary>
    ValueTask OnCompleteAsync(Exchange exchange);

    /// <summary>Called when the exchange failed.</summary>
    ValueTask OnFailureAsync(Exchange exchange);

    /// <summary>
    /// Whether the callback may be handed over to another exchange (default true), e.g. when <c>seda</c> passes a copy
    /// of the exchange to another route. Return false to keep it on the exchange it was registered with.
    /// </summary>
    bool AllowHandover => true;
}
