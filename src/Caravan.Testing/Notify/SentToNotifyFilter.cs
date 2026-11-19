using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing.Notify;

/// <summary>
/// Only exchanges that were sent to an endpoint matching the pattern (Camel's <c>wereSentTo</c>): their done events pass once a matching
/// send was seen.
/// </summary>
internal sealed class SentToNotifyFilter(ICaravanContext context, string pattern) : NotifyEventPredicate
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public override bool IsFilter => true;

    public override bool OnSent(Exchange exchange, CaravanEvent evt)
    {
        if (evt.Endpoint is { } endpoint && EndpointHelper.MatchEndpoint(context, endpoint.EndpointUri, pattern))
        {
            _ids.Add(exchange.ExchangeId);
        }

        return true;
    }

    public override bool OnCompleted(Exchange exchange, CaravanEvent evt) => _ids.Contains(exchange.ExchangeId);

    public override bool OnFailed(Exchange exchange, CaravanEvent evt) => _ids.Contains(exchange.ExchangeId);

    public override void Reset() => _ids.Clear();

    public override string ToString() => $"wereSentTo({pattern})";
}
