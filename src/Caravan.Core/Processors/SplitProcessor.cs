using System.Runtime.CompilerServices;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>split</c> EIP (Camel's <c>Splitter</c>): evaluates <see cref="Expression"/> on the input exchange and processes each part
/// with <see cref="Block"/>, on a correlated copy whose body is the part (or whose message is the part, when it is a
/// <see cref="Message"/>). Built on <see cref="MulticastProcessor"/>, which describes failures, parallel processing and aggregation.
/// </summary>
/// <remarks>
/// <para>
/// The expression may return a sequence (<see cref="IEnumerable{T}"/>, array, <see cref="IAsyncEnumerable{T}"/>, e.g. the lazy
/// tokens of <see cref="Expr.Tokenize(string, bool, int, bool)"/> or <see cref="Expr.Lines(int, bool)"/>), null (nothing to split) or
/// a single value. Unlike Camel, a string is not split on commas by default: it is a single part unless <see cref="Delimiter"/> is
/// set (use the tokenizer for anything more).
/// </para>
/// <para>
/// When not streaming, all parts are read first, so <see cref="ExchangePropertyKeys.SplitSize"/> is set on every part; when
/// streaming, parts are read one at a time as they are processed and the size is only set on the last part. Each part carries
/// <see cref="ExchangePropertyKeys.SplitIndex"/> and <see cref="ExchangePropertyKeys.SplitComplete"/>.
/// </para>
/// </remarks>
public sealed class SplitProcessor : MulticastProcessor
{
    /// <summary>Creates the processor.</summary>
    /// <param name="expression">Computes the parts.</param>
    /// <param name="block">Processes each part.</param>
    /// <param name="settings">The options; the default strategy of the EIP is <see cref="Aggregation.UseOriginalAggregationStrategy"/>.</param>
    /// <param name="delimiter">When set, a string value is split on it (parts trimmed, empty parts skipped).</param>
    public SplitProcessor(IExpression expression, IProcessor block, MulticastSettings settings, string? delimiter = null)
        : base([block], settings)
    {
        Expression = expression;
        Block = block;
        Delimiter = delimiter;
    }

    /// <summary>Computes the parts.</summary>
    public IExpression Expression { get; }

    /// <summary>Processes each part.</summary>
    public IProcessor Block { get; }

    /// <summary>When set, a string value is split on it.</summary>
    public string? Delimiter { get; }

    /// <inheritdoc />
    public override string ToString() => $"Split[{Expression} -> {Block}]";

    private protected override async IAsyncEnumerable<MulticastBranch> CreateBranchesAsync(Exchange original, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parts = SplitIterator.CreateAsync(Expression.Evaluate(original), Delimiter);
        if (Settings.Streaming)
        {
            await foreach (var part in parts.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new MulticastBranch(Block, CreatePart(original, part), null);
            }

            yield break;
        }

        var all = new List<object?>();
        await foreach (var part in parts.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            all.Add(part);
        }

        foreach (var part in all)
        {
            yield return new MulticastBranch(Block, CreatePart(original, part), all.Count);
        }
    }

    private protected override void UpdateNewExchange(Exchange exchange, int index, bool hasNext, MulticastBranch branch)
    {
        exchange.SetProperty(ExchangePropertyKeys.SplitIndex, index);
        if (branch.KnownSize is { } size)
        {
            exchange.SetProperty(ExchangePropertyKeys.SplitSize, size);
        }
        else if (!hasNext)
        {
            exchange.SetProperty(ExchangePropertyKeys.SplitSize, index + 1);
        }

        exchange.SetProperty(ExchangePropertyKeys.SplitComplete, !hasNext);
    }

    private static Exchange CreatePart(Exchange original, object? part)
    {
        var exchange = original.CreateCorrelatedCopy();
        if (part is Message message)
        {
            exchange.Message = message.Copy(exchange);
        }
        else
        {
            exchange.Message.Body = part;
        }

        return exchange;
    }
}
