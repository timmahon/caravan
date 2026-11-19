using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.DataTypes;

/// <summary>
/// Transforms with a data format (Camel's <c>dataFormatTransformer</c>): marshals when the target type's scheme is <see cref="Scheme"/>
/// (e.g. <c>clr:Order</c> → <c>json</c>), otherwise unmarshals (<c>json</c> → <c>clr:Order</c>).
/// </summary>
public sealed class DataFormatTransformer : ITransformer
{
    private readonly string? _dataFormatName;
    private IDataFormat? _dataFormat;

    /// <summary>Creates a transformer using a data format of the context (by name, e.g. <c>json</c>, resolved on first use).</summary>
    /// <param name="from">The source type.</param>
    /// <param name="to">The target type.</param>
    /// <param name="dataFormatName">The data format name; also the default <see cref="Scheme"/>.</param>
    /// <param name="scheme">The scheme the data format produces (default: <paramref name="dataFormatName"/>).</param>
    public DataFormatTransformer(DataType? from, DataType? to, string dataFormatName, string? scheme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFormatName);
        From = from;
        To = to;
        _dataFormatName = dataFormatName;
        Scheme = scheme ?? dataFormatName;
    }

    /// <summary>Creates a transformer using a data format instance.</summary>
    /// <param name="from">The source type.</param>
    /// <param name="to">The target type.</param>
    /// <param name="dataFormat">The data format.</param>
    /// <param name="scheme">The scheme the data format produces (<c>json</c>, <c>xml</c>, ...).</param>
    public DataFormatTransformer(DataType? from, DataType? to, IDataFormat dataFormat, string scheme)
    {
        ArgumentNullException.ThrowIfNull(dataFormat);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        From = from;
        To = to;
        _dataFormat = dataFormat;
        Scheme = scheme;
    }

    /// <inheritdoc />
    public DataType? From { get; }

    /// <inheritdoc />
    public DataType? To { get; }

    /// <inheritdoc />
    public string? Name { get; init; }

    /// <summary>The scheme of the data format's serialized form.</summary>
    public string Scheme { get; }

    /// <inheritdoc />
    public async ValueTask TransformAsync(Message message, DataType? from, DataType to)
    {
        var exchange = message.Exchange;
        var dataFormat = _dataFormat ??= exchange.Context.ResolveDataFormat(_dataFormatName!);
        IProcessor processor = string.Equals(to.Scheme, Scheme, StringComparison.Ordinal)
            ? new MarshalProcessor(dataFormat)
            : new UnmarshalProcessor(dataFormat);
        await processor.ProcessAsync(exchange).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"DataFormatTransformer[{From} -> {To}, {Scheme}]";
}
