using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>setHeaders</c> EIP: sets several headers, in order (each expression sees the headers set before it).</summary>
public sealed class SetHeadersProcessor(IReadOnlyList<KeyValuePair<string, IExpression>> headers) : IProcessor
{
    /// <summary>The headers and their expressions, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, IExpression>> Headers { get; } = headers;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var (name, expression) in Headers)
        {
            exchange.Message.SetHeader(name, expression.Evaluate(exchange));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetHeaders[{string.Join(", ", Headers.Select(h => h.Key))}]";
}
