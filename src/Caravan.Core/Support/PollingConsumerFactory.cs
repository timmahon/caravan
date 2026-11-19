using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Creates polling consumers for any endpoint.</summary>
public static class PollingConsumerFactory
{
    /// <summary>
    /// Creates a polling consumer (not started): the endpoint's own when it implements <see cref="IPollingConsumerEndpoint"/>,
    /// otherwise an <see cref="EventDrivenPollingConsumer"/>.
    /// </summary>
    public static IPollingConsumer Create(IEndpoint endpoint) =>
        endpoint is IPollingConsumerEndpoint polling ? polling.CreatePollingConsumer() : new EventDrivenPollingConsumer(endpoint);
}
