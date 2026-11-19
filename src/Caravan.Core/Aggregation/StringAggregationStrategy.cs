using System.Text;
using Caravan.Support;

namespace Caravan.Aggregation;

/// <summary>
/// Concatenates a value of each exchange (the body by default) into a string, separated by <see cref="Delimiter"/>, set as the
/// body of the first exchange when the aggregation completes (Camel's <c>StringAggregationStrategy</c>). Null values are skipped.
/// </summary>
public sealed class StringAggregationStrategy : IAggregationStrategy
{
    /// <summary>Creates the strategy.</summary>
    /// <param name="delimiter">Inserted between values; null for none.</param>
    /// <param name="pick">The value to take from each exchange; null for the body.</param>
    public StringAggregationStrategy(string? delimiter = null, IExpression? pick = null)
    {
        Delimiter = delimiter;
        Pick = pick ?? Expr.Body();
    }

    /// <summary>Inserted between values; null for none.</summary>
    public string? Delimiter { get; }

    /// <summary>The value taken from each exchange.</summary>
    public IExpression Pick { get; }

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        var result = oldExchange ?? newExchange;
        if (result.GetProperty(ExchangePropertyKeys.GroupedExchange) is not StringBuilder builder)
        {
            builder = new StringBuilder();
            result.SetProperty(ExchangePropertyKeys.GroupedExchange, builder);
        }

        if (ObjectHelper.ToText(newExchange, Pick.Evaluate(newExchange)) is { } value)
        {
            if (builder.Length > 0 && Delimiter is not null)
            {
                builder.Append(Delimiter);
            }

            builder.Append(value);
        }

        return result;
    }

    /// <inheritdoc />
    public void OnCompletion(Exchange exchange, Exchange? inputExchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.GroupedExchange) is StringBuilder builder)
        {
            exchange.RemoveProperty(ExchangePropertyKeys.GroupedExchange);
            exchange.Message.Body = builder.ToString();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"StringAggregationStrategy[{Delimiter}]";
}
