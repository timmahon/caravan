namespace Caravan.DataTypes;

/// <summary>
/// Transforms a message from one data type to another (Camel's <c>Transformer</c>). Registered in <see cref="ICaravanContext.Transformers"/> and
/// chosen by <see cref="From"/>/<see cref="To"/> (or <see cref="Name"/>) for route contracts and <c>transform</c> with <c>toType</c>.
/// </summary>
public interface ITransformer
{
    /// <summary>The source type; a type without name matches its whole scheme, null matches any type.</summary>
    DataType? From { get; }

    /// <summary>The target type; a type without name matches its whole scheme.</summary>
    DataType? To { get; }

    /// <summary>A name to find the transformer by (Camel 4's named transformers), or null.</summary>
    string? Name { get; }

    /// <summary>Transforms the message in place (body and headers).</summary>
    /// <param name="message">The message.</param>
    /// <param name="from">The message's current data type, if known.</param>
    /// <param name="to">The requested data type.</param>
    /// <returns>A task completing when done.</returns>
    ValueTask TransformAsync(Message message, DataType? from, DataType to);
}
