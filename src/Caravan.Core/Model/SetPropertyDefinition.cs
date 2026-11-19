using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sets an exchange property (<c>setProperty</c>).</summary>
public sealed class SetPropertyDefinition(string name, IExpression expression) : ProcessorDefinition
{
    /// <summary>The property name.</summary>
    public string Name { get; set; } = name;

    /// <summary>Computes the property value.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "setProperty";

    /// <inheritdoc />
    public override string Label => $"setProperty[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetPropertyProcessor(Name, Expression);
}
