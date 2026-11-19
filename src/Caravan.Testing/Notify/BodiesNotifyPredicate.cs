using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing.Notify;

/// <summary>
/// The bodies of the exchanges received (or done) are the expected ones, in order (Camel's <c>whenBodiesReceived</c>,
/// <c>whenExactBodiesReceived</c>, <c>whenBodiesDone</c>, <c>whenExactBodiesDone</c>). Exact: no more exchanges than bodies.
/// </summary>
internal sealed class BodiesNotifyPredicate(IReadOnlyList<object?> bodies, bool received, bool exact) : NotifyEventPredicate
{
    private int _count;
    private bool _allMatched = true;

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

    public override bool Matches() => _allMatched && (exact ? _count == bodies.Count : _count >= bodies.Count);

    public override void Reset()
    {
        _count = 0;
        _allMatched = true;
    }

    public override string ToString() =>
        $"when{(exact ? "Exact" : string.Empty)}Bodies{(received ? "Received" : "Done")}({string.Join(", ", bodies)})";

    private void Check(Exchange exchange)
    {
        var index = _count++;
        if (index < bodies.Count && !ObjectHelper.TypeCoerceEquals(exchange, exchange.Message.Body, bodies[index]))
        {
            _allMatched = false;
        }
    }
}
