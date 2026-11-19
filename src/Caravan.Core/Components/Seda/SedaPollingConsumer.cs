using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Seda;

/// <summary>Polls a <c>seda</c> queue directly; exchanges cancelled by their producer (timed out) are skipped.</summary>
internal sealed class SedaPollingConsumer(SedaEndpoint endpoint) : ServiceSupport, IPollingConsumer
{
    private static readonly Func<Exchange, bool> Accept = e => !e.CancellationToken.IsCancellationRequested;

    public IEndpoint Endpoint => endpoint;

    public async Task<Exchange> ReceiveAsync(CancellationToken cancellationToken = default) =>
        Adopt(await ChannelReceiver.ReceiveAsync(endpoint.Queue.Channel.Reader, Accept, cancellationToken).ConfigureAwait(false));

    public async Task<Exchange?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var exchange = await ChannelReceiver.ReceiveAsync(endpoint.Queue.Channel.Reader, Accept, timeout, endpoint.Context.TimeProvider, cancellationToken)
            .ConfigureAwait(false);
        return exchange is null ? null : Adopt(exchange);
    }

    public Exchange? ReceiveNoWait() => ChannelReceiver.TryTake(endpoint.Queue.Channel.Reader, Accept) is { } exchange ? Adopt(exchange) : null;

    public override string ToString() => $"SedaPollingConsumer[{endpoint.EndpointUri}]";

    private Exchange Adopt(Exchange exchange) => exchange.CopyToContext(endpoint.Context);
}
