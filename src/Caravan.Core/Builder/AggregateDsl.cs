using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>aggregate</c> EIP.</summary>
public static class AggregateDsl
{
    /// <summary>
    /// Aggregates exchanges with the same correlation key: <c>.Aggregate(Expr.Header("id"), AggregationStrategies.GroupedBody(),
    /// a =&gt; a.CompletionSize(3), then =&gt; then.To("mock:result"))</c>. Each completed group is sent through <paramref name="body"/>
    /// as a new exchange (with <see cref="ExchangePropertyKeys.AggregatedSize"/>, <see cref="ExchangePropertyKeys.AggregatedCompletedBy"/>
    /// and <see cref="ExchangePropertyKeys.AggregatedCorrelationKey"/>); the incoming exchanges continue unchanged.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="correlationExpression">Computes the correlation key.</param>
    /// <param name="aggregationStrategy">Combines the exchanges of a group; null to use <see cref="AggregateBuilder.AggregationStrategyRef"/>.</param>
    /// <param name="options">Sets the completion conditions and other options (at least one completion condition is required).</param>
    /// <param name="body">The steps run for each completed group.</param>
    public static T Aggregate<T>(this T steps, IExpression correlationExpression, IAggregationStrategy? aggregationStrategy, Action<AggregateBuilder> options, Action<RouteSteps> body)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(options);
        var definition = steps.AddStep(new AggregateDefinition(correlationExpression) { AggregationStrategy = aggregationStrategy });
        options(new AggregateBuilder(definition));
        steps.BuildNested(definition.Outputs, definition, body);
        return steps;
    }

    /// <summary>Aggregates exchanges with the same correlation key, computed by a function: <c>.Aggregate(e =&gt; e.Message.GetHeader("id"), ...)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="correlationExpression">Computes the correlation key.</param>
    /// <param name="aggregationStrategy">Combines the exchanges of a group; null to use <see cref="AggregateBuilder.AggregationStrategyRef"/>.</param>
    /// <param name="options">Sets the completion conditions and other options (at least one completion condition is required).</param>
    /// <param name="body">The steps run for each completed group.</param>
    public static T Aggregate<T>(this T steps, Func<Exchange, object?> correlationExpression, IAggregationStrategy? aggregationStrategy, Action<AggregateBuilder> options, Action<RouteSteps> body)
        where T : RouteSteps
        => steps.Aggregate(new FuncExpression(correlationExpression), aggregationStrategy, options, body);

    /// <summary>Aggregates exchanges with the same correlation key, computed from the body: <c>.Aggregate((Order o) =&gt; o.Customer, ...)</c>.</summary>
    /// <typeparam name="T">The builder type.</typeparam>
    /// <typeparam name="TBody">The type the body is converted to.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="correlationExpression">Computes the correlation key from the body.</param>
    /// <param name="aggregationStrategy">Combines the exchanges of a group; null to use <see cref="AggregateBuilder.AggregationStrategyRef"/>.</param>
    /// <param name="options">Sets the completion conditions and other options (at least one completion condition is required).</param>
    /// <param name="body">The steps run for each completed group.</param>
    public static T Aggregate<T, TBody>(this T steps, Func<TBody, object?> correlationExpression, IAggregationStrategy? aggregationStrategy, Action<AggregateBuilder> options, Action<RouteSteps> body)
        where T : RouteSteps
        => steps.Aggregate(Expr.Body(correlationExpression), aggregationStrategy, options, body);
}
