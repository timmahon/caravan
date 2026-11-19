using Caravan.Spi;

namespace Caravan.Testing.Notify;

/// <summary>
/// Base of the filters that select exchanges when they are created (by their input endpoint or route) and then let through only the
/// later events of those exchanges.
/// </summary>
internal abstract class ExchangeIdNotifyFilter : NotifyEventPredicate
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public override bool IsFilter => true;

    public override bool OnCreated(Exchange exchange, CaravanEvent evt)
    {
        if (!Accepts(exchange, evt))
        {
            return false;
        }

        _ids.Add(exchange.ExchangeId);
        return true;
    }

    public override bool OnCompleted(Exchange exchange, CaravanEvent evt) => _ids.Contains(exchange.ExchangeId);

    public override bool OnFailed(Exchange exchange, CaravanEvent evt) => _ids.Contains(exchange.ExchangeId);

    public override bool OnSent(Exchange exchange, CaravanEvent evt) => _ids.Contains(exchange.ExchangeId);

    public override void Reset() => _ids.Clear();

    /// <summary>Whether a newly created exchange is selected.</summary>
    protected abstract bool Accepts(Exchange exchange, CaravanEvent evt);
}
