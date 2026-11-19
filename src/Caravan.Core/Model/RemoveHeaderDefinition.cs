using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Removes a header (<c>removeHeader</c>).</summary>
public sealed class RemoveHeaderDefinition(string name) : ProcessorDefinition
{
    /// <summary>The header name (case-insensitive).</summary>
    public string Name { get; set; } = name;

    /// <inheritdoc />
    public override string ShortName => "removeHeader";

    /// <inheritdoc />
    public override string Label => $"removeHeader[{Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RemoveHeaderProcessor(Name);
}
