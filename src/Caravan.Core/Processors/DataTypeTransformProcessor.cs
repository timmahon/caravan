using Caravan.DataTypes;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>transform</c> EIP with <c>toType</c> (Camel 4): transforms the message to a data type with the context's transformers.</summary>
/// <param name="fromType">The type the message has (null: its <see cref="Message.DataType"/>).</param>
/// <param name="toType">The requested type.</param>
public sealed class DataTypeTransformProcessor(DataType? fromType, DataType toType) : IProcessor
{
    /// <summary>The type the message has, when given.</summary>
    public DataType? FromType { get; } = fromType;

    /// <summary>The requested type.</summary>
    public DataType ToType { get; } = toType;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange) => DataTypeTransformation.TransformAsync(exchange.Message, FromType, ToType);

    /// <inheritdoc />
    public override string ToString() => $"Transform[{FromType?.ToString() ?? "?"} -> {ToType}]";
}
