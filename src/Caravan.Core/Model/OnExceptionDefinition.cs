using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// An <c>onException</c> clause: what the error handler does when an exception of one of <see cref="ExceptionTypes"/> occurs.
/// Clauses defined on a route builder apply to all its routes; clauses defined on a route (<see cref="RouteScoped"/>) take
/// precedence. The best match is chosen as in Camel: the cause chain is walked from the innermost exception outwards, an exact
/// type match wins at once, otherwise the clause whose type is closest in the type hierarchy wins.
/// </summary>
/// <remarks>
/// After redelivery is exhausted the clause's steps (<see cref="OutputDefinition.Outputs"/>) run on the exchange with the exception
/// cleared and stored in <see cref="ExchangePropertyKeys.ExceptionCaught"/>. Then: <see cref="Handled"/> marks the failure handled
/// (the route stops, the caller sees success with the clause's result); <see cref="Continued"/> ignores the exception and routing
/// continues with the next node; otherwise the exception is restored and propagates to the caller. The clause's steps are not
/// wrapped by the error handler: a new exception thrown there fails the exchange with that exception.
/// </remarks>
public sealed class OnExceptionDefinition : OutputDefinition, IRedeliveryPolicyAware
{
    /// <summary>Creates a clause for the given exception types.</summary>
    /// <exception cref="ArgumentException">No type is given or a type is not an <see cref="Exception"/>.</exception>
    public OnExceptionDefinition(IEnumerable<Type> exceptionTypes)
    {
        ExceptionTypes = [.. exceptionTypes];
        if (ExceptionTypes.Count == 0)
        {
            throw new ArgumentException("onException needs at least one exception type", nameof(exceptionTypes));
        }

        foreach (var type in ExceptionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"{type} is not an exception type", nameof(exceptionTypes));
            }
        }
    }

    /// <summary>The exception types the clause matches (subtypes included).</summary>
    public List<Type> ExceptionTypes { get; }

    /// <summary>An extra condition for the clause to match.</summary>
    public IPredicate? OnWhen { get; set; }

    /// <summary>When it matches, the failure is handled: the caller sees success with the result of the clause's steps.</summary>
    public IPredicate? Handled { get; set; }

    /// <summary>When it matches, the exception is ignored and routing continues with the next node.</summary>
    public IPredicate? Continued { get; set; }

    /// <summary>When set, redelivery continues while it matches (instead of using the maximum redeliveries).</summary>
    public IPredicate? RetryWhile { get; set; }

    /// <inheritdoc />
    public RedeliveryPolicyDefinition RedeliveryPolicy { get; } = new();

    /// <summary>Called before each redelivery attempt (overrides the error handler's).</summary>
    public IProcessor? OnRedelivery { get; set; }

    /// <summary>Called right after each matching exception occurs (overrides the error handler's).</summary>
    public IProcessor? OnExceptionOccurred { get; set; }

    /// <summary>Runs the clause's steps on the original message (as received by the route's consumer).</summary>
    public bool UseOriginalMessage { get; set; }

    /// <summary>Runs the clause's steps on the original body, keeping the current headers.</summary>
    public bool UseOriginalBody { get; set; }

    /// <summary>True for a clause defined on a route; false for one defined on a route builder (applies to all its routes).</summary>
    public bool RouteScoped { get; set; }

    /// <inheritdoc />
    public override string ShortName => "onException";

    /// <inheritdoc />
    public override string Label => $"onException[{string.Join(", ", ExceptionTypes.Select(t => t.Name))}]";

    /// <summary>Builds the clause's steps (the error handler uses its own failure processor when the clause has no steps).</summary>
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
