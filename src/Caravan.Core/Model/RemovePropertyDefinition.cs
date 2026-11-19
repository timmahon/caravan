using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Removes an exchange property (<c>removeProperty</c>).</summary>
public sealed class RemovePropertyDefinition(string name) : ProcessorDefinition
{
    /// <summary>The property name.</summary>
    public string Name { get; set; } = name;

    /// <inheritdoc />
    public override string ShortName => "removeProperty";

    /// <inheritdoc />
    public override string Label => $"removeProperty[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RemovePropertyProcessor(Name);
}
