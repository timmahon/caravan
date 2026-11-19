using Caravan.Support;

namespace Caravan.DataTypes;

/// <summary>
/// The data type transformers of a context (Camel's <c>TransformerRegistry</c>), used by route contracts (<c>inputType</c>/<c>outputType</c>)
/// and <c>transform</c> with <c>toType</c>. <see cref="Resolve"/> prefers exact types over scheme-only ones and a specific source over any source.
/// </summary>
public sealed class TransformerRegistry
{
    private readonly CopyOnWriteList<ITransformer> _transformers = new();

    /// <summary>Creates the registry of a context.</summary>
    /// <param name="context">The context.</param>
    public TransformerRegistry(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>The transformers, in the order they were added.</summary>
    public IReadOnlyList<ITransformer> All => [.. _transformers];

    /// <summary>Adds a transformer.</summary>
    /// <param name="transformer">The transformer.</param>
    /// <returns>This registry.</returns>
    public TransformerRegistry Add(ITransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _transformers.Add(transformer);
        return this;
    }

    /// <summary>Adds a transformer given as an action on the message: <c>Add("xml:Order", "json:Order", m =&gt; ...)</c>.</summary>
    /// <param name="from">The source type (<c>scheme:name</c>), or null for any.</param>
    /// <param name="to">The target type.</param>
    /// <param name="transform">Transforms the message in place.</param>
    /// <returns>This registry.</returns>
    public TransformerRegistry Add(string? from, string to, Action<Message> transform) =>
        Add(new DelegateTransformer(from is null ? null : DataType.Parse(from), DataType.Parse(to), transform));

    /// <summary>Removes a transformer.</summary>
    /// <param name="transformer">The transformer.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Remove(ITransformer transformer) => _transformers.Remove(transformer);

    /// <summary>Finds the transformer named <paramref name="name"/>, or null.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The transformer.</returns>
    public ITransformer? ResolveByName(string name) => _transformers.LastOrDefault(t => t.Name == name);

    /// <summary>Finds the best transformer from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    /// <param name="from">The current type.</param>
    /// <param name="to">The requested type.</param>
    /// <returns>The transformer.</returns>
    public ITransformer? Resolve(DataType from, DataType to)
    {
        ITransformer? best = null;
        var bestScore = -1;
        foreach (var transformer in _transformers)
        {
            var score = Score(transformer, from, to);
            if (score > bestScore)
            {
                best = transformer;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Finds two transformers chaining <paramref name="from"/> to <paramref name="to"/> through an intermediate type (Camel's transformer chain).</summary>
    /// <param name="from">The current type.</param>
    /// <param name="to">The requested type.</param>
    /// <returns>The two transformers, or null.</returns>
    public (ITransformer First, DataType Via, ITransformer Second)? ResolveChain(DataType from, DataType to)
    {
        foreach (var first in _transformers)
        {
            if (first.To is not { } via || !Accepts(first.From, from) || via == to)
            {
                continue;
            }

            if (Resolve(via, to) is { } second && !ReferenceEquals(second, first))
            {
                return (first, via, second);
            }
        }

        return null;
    }

    private static int Score(ITransformer transformer, DataType from, DataType to)
    {
        if (transformer.To is not { } target || !target.Matches(to) || !Accepts(transformer.From, from))
        {
            return -1;
        }

        var toScore = target.Name is null ? 1 : 2;
        var fromScore = transformer.From is null ? 0 : transformer.From.Name is null ? 1 : 2;
        return (fromScore * 3) + toScore;
    }

    private static bool Accepts(DataType? source, DataType from) => source is null || source.Matches(from);
}
