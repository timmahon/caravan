namespace Caravan.Aggregation;

/// <summary>
/// A configurable aggregation strategy (a lighter version of Camel's <c>FlexibleAggregationStrategy</c>): picks a value from each
/// exchange (the body by default), optionally only from exchanges matching a condition, converts it to <typeparamref name="T"/>
/// and stores it, or a <see cref="List{T}"/> of all values with <see cref="AccumulateInCollection"/>, in the body, a header or a
/// property of the result. The result is a correlated copy of the first exchange.
/// </summary>
/// <typeparam name="T">The type values are converted to.</typeparam>
public sealed class FlexibleAggregationStrategy<T> : IAggregationStrategy
{
    private IExpression _pick = Expr.Body();
    private IPredicate? _condition;
    private bool _collection;
    private bool _storeNulls;
    private bool _ignoreInvalidCasts;
    private (int Kind, string? Name) _target;
    private Action<Exchange>? _onCompletion;

    /// <summary>Picks the value to aggregate from each exchange (default: the body).</summary>
    /// <param name="expression">The value to pick.</param>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> Pick(IExpression expression)
    {
        _pick = expression;
        return this;
    }

    /// <summary>Only aggregates exchanges matching <paramref name="predicate"/>; the others are ignored.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> Condition(IPredicate predicate)
    {
        _condition = predicate;
        return this;
    }

    /// <summary>Accumulates all values in a <see cref="List{T}"/> instead of keeping the latest one.</summary>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> AccumulateInCollection()
    {
        _collection = true;
        return this;
    }

    /// <summary>Also stores null values (they are skipped by default).</summary>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> StoreNulls()
    {
        _storeNulls = true;
        return this;
    }

    /// <summary>Uses null instead of failing when a value cannot be converted to <typeparamref name="T"/>.</summary>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> IgnoreInvalidCasts()
    {
        _ignoreInvalidCasts = true;
        return this;
    }

    /// <summary>Stores the result in the body (the default).</summary>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> StoreInBody()
    {
        _target = (0, null);
        return this;
    }

    /// <summary>Stores the result in the header <paramref name="name"/>.</summary>
    /// <param name="name">The header name.</param>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> StoreInHeader(string name)
    {
        _target = (1, name);
        return this;
    }

    /// <summary>Stores the result in the exchange property <paramref name="name"/>.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> StoreInProperty(string name)
    {
        _target = (2, name);
        return this;
    }

    /// <summary>Runs <paramref name="callback"/> on the result when the aggregation completes.</summary>
    /// <param name="callback">The callback.</param>
    /// <returns>This strategy.</returns>
    public FlexibleAggregationStrategy<T> OnCompletion(Action<Exchange> callback)
    {
        _onCompletion = callback;
        return this;
    }

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        var result = oldExchange;
        if (result is null)
        {
            result = newExchange.CreateCorrelatedCopy();
            Store(result, _collection ? new List<T?>() : null);
        }

        if (_condition is not null && !_condition.Matches(newExchange))
        {
            return result;
        }

        var picked = _pick.Evaluate(newExchange);
        if (picked is null && !_storeNulls)
        {
            return result;
        }

        var value = picked is null ? default : Convert(newExchange, picked);

        if (_collection)
        {
            if (Load(result) is not List<T?> list)
            {
                list = [];
                Store(result, list);
            }

            list.Add(value);
        }
        else
        {
            Store(result, value);
        }

        return result;
    }

    /// <inheritdoc />
    void IAggregationStrategy.OnCompletion(Exchange exchange, Exchange? inputExchange) => _onCompletion?.Invoke(exchange);

    /// <inheritdoc />
    public override string ToString() => $"FlexibleAggregationStrategy<{typeof(T).Name}>";

    private T? Convert(Exchange exchange, object value)
    {
        if (value is T typed)
        {
            return typed;
        }

        try
        {
            return exchange.Context.TypeConverter.Convert(typeof(T), exchange, value) is T converted ? converted : default;
        }
        catch (Exception) when (_ignoreInvalidCasts)
        {
            return default;
        }
    }

    private object? Load(Exchange exchange) => _target.Kind switch
    {
        1 => exchange.Message.GetHeader(_target.Name!),
        2 => exchange.GetProperty(_target.Name!),
        _ => exchange.Message.Body,
    };

    private void Store(Exchange exchange, object? value)
    {
        switch (_target.Kind)
        {
            case 1:
                exchange.Message.SetHeader(_target.Name!, value);
                break;
            case 2:
                exchange.SetProperty(_target.Name!, value);
                break;
            default:
                exchange.Message.Body = value;
                break;
        }
    }
}
