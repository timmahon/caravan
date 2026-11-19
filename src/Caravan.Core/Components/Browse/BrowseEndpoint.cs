using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Browse;

/// <summary>
/// A <c>browse</c> endpoint: records a copy of every exchange sent to it (in arrival order), then hands the exchange to its consumer, if any.
/// </summary>
public sealed class BrowseEndpoint : EndpointBase
{
    private readonly List<Exchange> _exchanges = [];
    private volatile IProcessor? _consumer;

    internal BrowseEndpoint(string uri, BrowseComponent component, string name)
        : base(uri, component, component.Context!)
    {
        Name = name;
    }

    /// <summary>The endpoint name.</summary>
    public string Name { get; }

    /// <summary>Keep at most this many exchanges, dropping the oldest (default 0: unbounded; Camel's <c>browseLimit</c>).</summary>
    public int BrowseLimit { get; set; }

    /// <summary>A snapshot of the recorded exchanges (copies taken when they arrived).</summary>
    public IReadOnlyList<Exchange> Exchanges
    {
        get
        {
            lock (_exchanges)
            {
                return [.. _exchanges];
            }
        }
    }

    /// <summary>Forgets the recorded exchanges.</summary>
    public void Clear()
    {
        lock (_exchanges)
        {
            _exchanges.Clear();
        }
    }

    /// <inheritdoc />
    public override IProducer CreateProducer() => new BrowseProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new BrowseConsumer(this, processor);

    internal void SetConsumer(IProcessor? processor) => _consumer = processor;

    internal async ValueTask OnExchangeAsync(Exchange exchange)
    {
        lock (_exchanges)
        {
            _exchanges.Add(exchange.Copy());
            if (BrowseLimit > 0 && _exchanges.Count > BrowseLimit)
            {
                _exchanges.RemoveAt(0);
            }
        }

        if (_consumer is { } consumer)
        {
            await consumer.ProcessAsync(exchange).ConfigureAwait(false);
        }
    }
}
