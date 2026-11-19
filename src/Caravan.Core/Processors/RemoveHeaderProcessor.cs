using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Removes one header (<c>removeHeader</c>).</summary>
public sealed class RemoveHeaderProcessor(string name) : IProcessor
{
    /// <summary>The header name (case-insensitive).</summary>
    public string Name { get; } = name;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.RemoveHeader(Name);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"RemoveHeader[{Name}]";
}
