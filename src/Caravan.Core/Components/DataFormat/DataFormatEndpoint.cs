using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.DataFormat;

/// <summary>A <c>dataformat</c> endpoint: its producer marshals or unmarshals the body with the data format.</summary>
public sealed class DataFormatEndpoint : EndpointBase
{
    internal DataFormatEndpoint(string uri, DataFormatComponent component, string name, bool marshal, IDataFormat dataFormat)
        : base(uri, component, component.Context!)
    {
        Name = name;
        IsMarshal = marshal;
        DataFormat = dataFormat;
    }

    /// <summary>The data format name.</summary>
    public string Name { get; }

    /// <summary>True to marshal, false to unmarshal.</summary>
    public bool IsMarshal { get; }

    /// <summary>The configured data format.</summary>
    public IDataFormat DataFormat { get; }

    /// <summary>With unmarshal: leave a null body as is instead of failing.</summary>
    public bool AllowNullBody { get; set; }

    /// <inheritdoc />
    public override IProducer CreateProducer() =>
        new DataFormatProducer(this, IsMarshal ? new MarshalProcessor(DataFormat) : new UnmarshalProcessor(DataFormat, AllowNullBody));

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The dataformat component does not support consumers");

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken) => StartServiceAsync(DataFormat, cancellationToken);

    /// <inheritdoc />
    protected override Task DoStopAsync(CancellationToken cancellationToken) => StopServiceAsync(DataFormat, cancellationToken);
}
