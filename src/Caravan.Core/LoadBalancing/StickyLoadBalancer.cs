namespace Caravan.LoadBalancing;

/// <summary>
/// Sends exchanges with the same correlation value to the same processor (Camel's <c>StickyLoadBalancer</c>): a value seen for the first time is
/// assigned the next processor in round-robin order; exchanges whose value is null are balanced round robin without sticking. As in Camel the
/// assignments are kept for the lifetime of the load balancer. Thread-safe.
/// </summary>
/// <param name="correlationExpression">Computes the correlation value.</param>
public sealed class StickyLoadBalancer(IExpression correlationExpression) : QueueLoadBalancer
{
    private readonly Dictionary<object, int> _sticky = [];
    private readonly Lock _lock = new();
    private int _next;

    /// <summary>Computes the correlation value.</summary>
    public IExpression CorrelationExpression { get; } = correlationExpression;

    /// <summary>The number of correlation values assigned to a processor.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _sticky.Count;
            }
        }
    }

    /// <inheritdoc />
    protected override int ChooseIndex(Exchange exchange, int count)
    {
        var key = CorrelationExpression.Evaluate(exchange);
        lock (_lock)
        {
            if (key is not null && _sticky.TryGetValue(key, out var assigned) && assigned < count)
            {
                return assigned;
            }

            var index = _next % count;
            _next = index + 1;
            if (key is not null)
            {
                _sticky[key] = index;
            }

            return index;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"StickyLoadBalancer[{CorrelationExpression}]";
}
