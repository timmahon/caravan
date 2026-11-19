using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Removes the variables matching a name or pattern (<c>removeVariable</c>).</summary>
public sealed class RemoveVariableProcessor(string name) : IProcessor
{
    /// <summary>The name or pattern.</summary>
    public string Name { get; } = name;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.RemoveVariable(Name);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"RemoveVariable[{Name}]";
}
