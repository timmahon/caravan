using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Converts the body to <see cref="Type"/> (<c>convertBodyTo</c>); see <see cref="ConvertBodyProcessor"/>.</summary>
public sealed class ConvertBodyDefinition(Type type) : ProcessorDefinition
{
    /// <summary>The target type.</summary>
    public Type Type { get; set; } = type;

    /// <summary>The charset used between text and bytes/streams; null uses UTF-8.</summary>
    public string? Charset { get; set; }

    /// <summary>Whether a failed conversion fails the exchange (default true).</summary>
    public bool Mandatory { get; set; } = true;

    /// <inheritdoc />
    public override string ShortName => "convertBodyTo";

    /// <inheritdoc />
    public override string Label => $"convertBodyTo[{Type.Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ConvertBodyProcessor(Type, Charset, Mandatory);
}
