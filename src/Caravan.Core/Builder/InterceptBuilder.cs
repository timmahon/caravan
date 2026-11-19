using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Builds an <c>intercept</c> or <c>interceptFrom</c> block: <c>Intercept(i =&gt; i.When(p).To("mock:intercepted").Stop())</c>.
/// </summary>
public sealed class InterceptBuilder : RouteSteps
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The intercept being built.</param>
    /// <param name="route">The route it belongs to, if any.</param>
    public InterceptBuilder(InterceptDefinition definition, RouteDefinition? route = null)
        : base(definition.Outputs, route, definition)
    {
        Definition = definition;
    }

    /// <summary>The intercept being built.</summary>
    public InterceptDefinition Definition { get; }

    /// <summary>Runs the block only for exchanges matching <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public InterceptBuilder When(IPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Definition.When = predicate;
        return this;
    }

    /// <summary>Runs the block only for exchanges matching <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public InterceptBuilder When(Func<Exchange, bool> predicate) => When(Pred.From(predicate));
}
