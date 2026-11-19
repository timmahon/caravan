using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Removes one exchange property (<c>removeProperty</c>).</summary>
public sealed class RemovePropertyProcessor(string name) : IProcessor
{
    /// <summary>The property name (case-sensitive).</summary>
    public string Name { get; } = name;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.RemoveProperty(Name);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"RemoveProperty[{Name}]";
}
