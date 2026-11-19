using Caravan.Spi;

namespace Caravan.Testing.Notify;

/// <summary>
/// One condition or filter of a <see cref="NotifyBuilder"/> (Camel's <c>EventPredicate</c>). Events reach the predicates of a group in order;
/// a predicate returning false stops the event there, which is how filters (<c>from</c>, <c>fromRoute</c>, <c>filter</c>,
/// <c>wereSentTo</c>) restrict the conditions after them. Called under the builder's lock.
/// </summary>
internal abstract class NotifyEventPredicate
{
    /// <summary>Whether this is a filter: it restricts the events seen by later predicates but is not a condition itself.</summary>
    public virtual bool IsFilter => false;

    /// <summary>An exchange was created by a route consumer (a new unit of work).</summary>
    public virtual bool OnCreated(Exchange exchange, CaravanEvent evt) => true;

    /// <summary>An exchange completed successfully.</summary>
    public virtual bool OnCompleted(Exchange exchange, CaravanEvent evt) => true;

    /// <summary>An exchange failed.</summary>
    public virtual bool OnFailed(Exchange exchange, CaravanEvent evt) => true;

    /// <summary>An exchange was sent to an endpoint.</summary>
    public virtual bool OnSent(Exchange exchange, CaravanEvent evt) => true;

    /// <summary>Whether the condition holds (filters: always true).</summary>
    public virtual bool Matches() => true;

    /// <summary>Forgets what was seen.</summary>
    public abstract void Reset();
}
