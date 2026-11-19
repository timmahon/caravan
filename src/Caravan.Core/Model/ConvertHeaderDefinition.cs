using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Converts a header to <see cref="Type"/> (<c>convertHeaderTo</c>); see <see cref="ConvertHeaderProcessor"/>.</summary>
public sealed class ConvertHeaderDefinition(string name, Type type) : ProcessorDefinition
{
    /// <summary>The header to convert.</summary>
    public string Name { get; set; } = name;

    /// <summary>The target type.</summary>
    public Type Type { get; set; } = type;

    /// <summary>The header receiving the converted value; null replaces <see cref="Name"/>.</summary>
    public string? ToName { get; set; }

    /// <summary>Whether a failed conversion fails the exchange (default true).</summary>
    public bool Mandatory { get; set; } = true;

    /// <inheritdoc />
    public override string ShortName => "convertHeaderTo";

    /// <inheritdoc />
    public override string Label => $"convertHeaderTo[{Name}, {Type.Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ConvertHeaderProcessor(Name, Type, ToName, Mandatory);
}
