using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>Sends the exchange to an endpoint (the <c>to</c> EIP).</summary>
public sealed class SendProcessor(IEndpoint endpoint, ExchangePattern? pattern = null) : ServiceSupport, IProcessor
{
    private IProducer? _producer;

    /// <summary>The target endpoint.</summary>
    public IEndpoint Endpoint { get; } = endpoint;

    /// <summary>The exchange pattern used while sending; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern { get; } = pattern;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var producer = _producer ?? throw new InvalidOperationException($"{this} is not started");
        var original = exchange.Pattern;
        if (Pattern is { } p)
        {
            exchange.Pattern = p;
        }

        exchange.SetProperty(ExchangePropertyKeys.ToEndpoint, Endpoint.EndpointUri);
        var context = exchange.Context;
        EventHelper.Notify(context, CaravanEventType.ExchangeSending, exchange, Endpoint);
        var start = context.TimeProvider.GetTimestamp();
        try
        {
            await producer.ProcessAsync(exchange).ConfigureAwait(false);
        }
        finally
        {
            exchange.Pattern = original;
            EventHelper.Notify(
                context,
                new CaravanEvent(CaravanEventType.ExchangeSent, context)
                {
                    Exchange = exchange,
                    Endpoint = Endpoint,
                    Duration = context.TimeProvider.GetElapsedTime(start),
                });
        }
    }

    /// <summary>Starts the endpoint and creates and starts the producer.</summary>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        await Endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        _producer = SendToEndpointInterceptors.CreateProducer(Endpoint);
        await _producer.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the producer.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (_producer is not null)
        {
            await _producer.StopAsync(cancellationToken).ConfigureAwait(false);
            _producer = null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"SendTo[{Endpoint.EndpointUri}]";
}
