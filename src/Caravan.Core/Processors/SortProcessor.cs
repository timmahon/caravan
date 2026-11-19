using System.Collections;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>sort</c> EIP (Camel's <c>SortProcessor</c>): evaluates <see cref="Expression"/> (by default the body) to a sequence, sorts its items with
/// <see cref="Comparer"/> (by default <see cref="ObjectHelper.TypeCoerceCompare"/>, Camel's natural ordering) and sets the body to the sorted
/// <see cref="List{T}"/> of objects. A string is a single item unless the expression splits it (e.g. <c>Expr.Tokenize("\n")</c>); null gives an
/// empty list. The sort is stable.
/// </summary>
public sealed class SortProcessor(IExpression expression, IComparer<object?>? comparer = null) : IProcessor
{
    /// <summary>Computes the items to sort.</summary>
    public IExpression Expression { get; } = expression;

    /// <summary>Orders the items; null for <see cref="ObjectHelper.TypeCoerceCompare"/>.</summary>
    public IComparer<object?>? Comparer { get; } = comparer;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var items = Expression.Evaluate(exchange) switch
        {
            null => [],
            string text => [text],
            IEnumerable enumerable => enumerable.Cast<object?>().ToList(),
            var single => new List<object?> { single },
        };
        var comparer = Comparer ?? Comparer<object?>.Create((x, y) => ObjectHelper.TypeCoerceCompare(exchange, x, y));
        exchange.Message.Body = items.Order(comparer).ToList();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Sort[{Expression}]";
}
