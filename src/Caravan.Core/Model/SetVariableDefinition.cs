using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sets a variable (<c>setVariable</c>, Camel 4): an exchange variable, or <c>global:name</c>, <c>route:name</c>, ...</summary>
public sealed class SetVariableDefinition(string name, IExpression expression) : ProcessorDefinition
{
    /// <summary>The variable name, optionally prefixed by a repository id.</summary>
    public string Name { get; set; } = name;

    /// <summary>Computes the value.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "setVariable";

    /// <inheritdoc />
    public override string Label => $"setVariable[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetVariableProcessor(Name, Expression);
}
