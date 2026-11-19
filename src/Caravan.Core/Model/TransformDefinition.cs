using Caravan.DataTypes;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Transforms the body (<c>transform</c>): with an <see cref="Expression"/> the same runtime behaviour as <see cref="SetBodyDefinition"/>; with
/// <see cref="ToType"/> (Camel 4 data types) the message is transformed with the context's transformers (see <see cref="TransformerRegistry"/>).
/// </summary>
public sealed class TransformDefinition : ProcessorDefinition
{
    /// <summary>Creates a transform computing the new body.</summary>
    /// <param name="expression">Computes the new body.</param>
    public TransformDefinition(IExpression expression)
    {
        Expression = expression;
    }

    /// <summary>Creates a data type transform.</summary>
    /// <param name="toType">The requested data type.</param>
    /// <param name="fromType">The current data type (null: the message's).</param>
    public TransformDefinition(DataType toType, DataType? fromType = null)
    {
        ToType = toType;
        FromType = fromType;
    }

    /// <summary>Computes the new body; null for a data type transform.</summary>
    public IExpression? Expression { get; set; }

    /// <summary>The requested data type (data type transform).</summary>
    public DataType? ToType { get; set; }

    /// <summary>The current data type (data type transform); null uses the message's.</summary>
    public DataType? FromType { get; set; }

    /// <inheritdoc />
    public override string ShortName => "transform";

    /// <inheritdoc />
    public override string Label => ToType is null ? $"transform[{Expression}]" : $"transform[{FromType?.ToString() ?? "?"} -> {ToType}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        ToType is { } toType
            ? new DataTypeTransformProcessor(FromType, toType)
            : new SetBodyProcessor(Expression ?? throw new InvalidOperationException("transform needs an expression or a toType"));
}
