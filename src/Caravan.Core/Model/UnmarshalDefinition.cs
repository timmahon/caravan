using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Unmarshals the body with a data format (<c>unmarshal</c>); see <see cref="UnmarshalProcessor"/>.</summary>
public sealed class UnmarshalDefinition : ProcessorDefinition
{
    /// <summary>The data format instance; when null <see cref="DataFormatName"/> is resolved when the route is created.</summary>
    public IDataFormat? DataFormat { get; set; }

    /// <summary>The name of the data format (<see cref="ICaravanContext.ResolveDataFormat"/>), used when <see cref="DataFormat"/> is null.</summary>
    public string? DataFormatName { get; set; }

    /// <summary>Whether a null body is left as is instead of failing (default false, as in Camel).</summary>
    public bool AllowNullBody { get; set; }

    /// <inheritdoc />
    public override string ShortName => "unmarshal";

    /// <inheritdoc />
    public override string Label => $"unmarshal[{DataFormatName ?? DataFormat?.ToString()}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new UnmarshalProcessor(DataFormatDefinitions.Resolve(routeContext, DataFormat, DataFormatName), AllowNullBody);
}
