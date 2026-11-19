using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Removes variables (<c>removeVariable</c>): a name, <c>*</c>, a wildcard or a regular expression, optionally prefixed (<c>global:*</c>).</summary>
public sealed class RemoveVariableDefinition(string name) : ProcessorDefinition
{
    /// <summary>The name or pattern.</summary>
    public string Name { get; set; } = name;

    /// <inheritdoc />
    public override string ShortName => "removeVariable";

    /// <inheritdoc />
    public override string Label => $"removeVariable[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RemoveVariableProcessor(Name);
}
