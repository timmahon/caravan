namespace Caravan.Spi;

/// <summary>
/// An endpoint that creates polling consumers. Endpoints derived from <see cref="Support.EndpointBase"/> implement it
/// with an event-driven adapter by default; components with a natural pull model (e.g. <c>seda</c>) override it.
/// Use <see cref="Support.PollingConsumerFactory.Create"/> to poll any endpoint.
/// </summary>
public interface IPollingConsumerEndpoint : IEndpoint
{
    /// <summary>Creates a polling consumer for this endpoint (not started).</summary>
    IPollingConsumer CreatePollingConsumer();
}
