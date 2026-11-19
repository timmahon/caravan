using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Converts a variable to <see cref="Type"/> (<c>convertVariableTo</c>); see <see cref="ConvertVariableProcessor"/>.</summary>
public sealed class ConvertVariableDefinition(string name, Type type) : ProcessorDefinition
{
    /// <summary>The variable to convert.</summary>
    public string Name { get; set; } = name;

    /// <summary>The target type.</summary>
    public Type Type { get; set; } = type;

    /// <summary>The variable receiving the converted value; null replaces <see cref="Name"/>.</summary>
    public string? ToName { get; set; }

    /// <summary>Whether a failed conversion fails the exchange (default true).</summary>
    public bool Mandatory { get; set; } = true;

    /// <inheritdoc />
    public override string ShortName => "convertVariableTo";

    /// <inheritdoc />
    public override string Label => $"convertVariableTo[{Name}, {Type.Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ConvertVariableProcessor(Name, Type, ToName, Mandatory);
}
