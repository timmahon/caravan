using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sets several variables in order (<c>setVariables</c>); each expression sees the variables set before it.</summary>
public sealed class SetVariablesDefinition : ProcessorDefinition
{
    /// <summary>The variables to set: name and expression.</summary>
    public List<KeyValuePair<string, IExpression>> Variables { get; } = [];

    /// <inheritdoc />
    public override string ShortName => "setVariables";

    /// <inheritdoc />
    public override string Label => $"setVariables[{string.Join(", ", Variables.Select(v => v.Key))}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new Pipeline([.. Variables.Select(v => (IProcessor)new SetVariableProcessor(v.Key, v.Value))]);
}
