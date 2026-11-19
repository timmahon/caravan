using Caravan.Spi;

namespace Caravan.Testing.Notify;

/// <summary>
/// Any (or all, and at least one) of the exchanges received (or done) match a predicate (Camel's <c>whenAnyReceivedMatches</c>,
/// <c>whenAllReceivedMatches</c>, <c>whenAnyDoneMatches</c>, <c>whenAllDoneMatches</c>).
/// </summary>
internal sealed class ExchangeMatchesNotifyPredicate(IPredicate predicate, bool received, bool all) : NotifyEventPredicate
{
    private int _seen;
    private int _matched;

    public override bool OnCreated(Exchange exchange, CaravanEvent evt)
    {
        if (received)
        {
            Check(exchange);
        }

        return true;
    }

    public override bool OnCompleted(Exchange exchange, CaravanEvent evt)
    {
        if (!received)
        {
            Check(exchange);
        }

        return true;
    }

    public override bool OnFailed(Exchange exchange, CaravanEvent evt) => OnCompleted(exchange, evt);

    public override bool Matches() => all ? _seen > 0 && _matched == _seen : _matched > 0;

    public override void Reset()
    {
        _seen = 0;
        _matched = 0;
    }

    public override string ToString() => $"when{(all ? "All" : "Any")}{(received ? "Received" : "Done")}Matches({predicate})";

    private void Check(Exchange exchange)
    {
        _seen++;
        bool matches;
        try
        {
            matches = predicate.Matches(exchange);
        }
        catch (Exception)
        {
            matches = false;
        }

        if (matches)
        {
            _matched++;
        }
    }
}
