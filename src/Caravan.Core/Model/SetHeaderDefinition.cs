using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sets a header (<c>setHeader</c>).</summary>
public sealed class SetHeaderDefinition(string name, IExpression expression) : ProcessorDefinition
{
    /// <summary>The header name.</summary>
    public string Name { get; set; } = name;

    /// <summary>Computes the header value.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "setHeader";

    /// <inheritdoc />
    public override string Label => $"setHeader[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetHeaderProcessor(Name, Expression);
}
