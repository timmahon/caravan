using Caravan.Spi;

namespace Caravan.Testing.Notify;

/// <summary>Only events of exchanges matching a predicate at the time of the event (Camel's <c>filter</c>).</summary>
internal sealed class ExchangeFilterNotifyPredicate(IPredicate predicate) : NotifyEventPredicate
{
    public override bool IsFilter => true;

    public override bool OnCreated(Exchange exchange, CaravanEvent evt) => Test(exchange);

    public override bool OnCompleted(Exchange exchange, CaravanEvent evt) => Test(exchange);

    public override bool OnFailed(Exchange exchange, CaravanEvent evt) => Test(exchange);

    public override bool OnSent(Exchange exchange, CaravanEvent evt) => Test(exchange);

    public override void Reset()
    {
    }

    public override string ToString() => $"filter({predicate})";

    private bool Test(Exchange exchange)
    {
        try
        {
            return predicate.Matches(exchange);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
