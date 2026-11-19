using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Marshals the body with a data format (<c>marshal</c>); see <see cref="MarshalProcessor"/>.</summary>
public sealed class MarshalDefinition : ProcessorDefinition
{
    /// <summary>The data format instance; when null <see cref="DataFormatName"/> is resolved when the route is created.</summary>
    public IDataFormat? DataFormat { get; set; }

    /// <summary>The name of the data format (<see cref="ICaravanContext.ResolveDataFormat"/>), used when <see cref="DataFormat"/> is null.</summary>
    public string? DataFormatName { get; set; }

    /// <inheritdoc />
    public override string ShortName => "marshal";

    /// <inheritdoc />
    public override string Label => $"marshal[{DataFormatName ?? DataFormat?.ToString()}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new MarshalProcessor(DataFormatDefinitions.Resolve(routeContext, DataFormat, DataFormatName));
}
