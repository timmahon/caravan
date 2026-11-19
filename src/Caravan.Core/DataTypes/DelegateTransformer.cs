namespace Caravan.DataTypes;

/// <summary>A transformer given as a function (Camel's custom transformer as a lambda).</summary>
public sealed class DelegateTransformer : ITransformer
{
    private readonly Func<Message, DataType?, DataType, ValueTask> _transform;

    /// <summary>Creates the transformer.</summary>
    /// <param name="from">The source type (null: any).</param>
    /// <param name="to">The target type.</param>
    /// <param name="transform">Transforms the message in place.</param>
    /// <param name="name">An optional name.</param>
    public DelegateTransformer(DataType? from, DataType? to, Func<Message, DataType?, DataType, ValueTask> transform, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (to is null && name is null)
        {
            throw new ArgumentException("A transformer needs a target type or a name");
        }

        From = from;
        To = to;
        Name = name;
        _transform = transform;
    }

    /// <summary>Creates a transformer from a synchronous action.</summary>
    /// <param name="from">The source type (null: any).</param>
    /// <param name="to">The target type.</param>
    /// <param name="transform">Transforms the message in place.</param>
    public DelegateTransformer(DataType? from, DataType to, Action<Message> transform)
        : this(from, to, (m, _, _) =>
        {
            transform(m);
            return ValueTask.CompletedTask;
        })
    {
    }

    /// <inheritdoc />
    public DataType? From { get; }

    /// <inheritdoc />
    public DataType? To { get; }

    /// <inheritdoc />
    public string? Name { get; }

    /// <inheritdoc />
    public ValueTask TransformAsync(Message message, DataType? from, DataType to) => _transform(message, from, to);

    /// <inheritdoc />
    public override string ToString() => $"Transformer[{From?.ToString() ?? "*"} -> {To?.ToString() ?? Name}]";
}
