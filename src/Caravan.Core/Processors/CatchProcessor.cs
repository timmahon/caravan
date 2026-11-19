using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>A <c>doCatch</c> clause: decides whether it catches an exception and holds the clause's block.</summary>
public sealed class CatchProcessor(IReadOnlyList<Type> exceptionTypes, IPredicate? onWhen, IProcessor block) : INavigableProcessor
{
    /// <summary>The exception types caught.</summary>
    public IReadOnlyList<Type> ExceptionTypes { get; } = exceptionTypes;

    /// <summary>The extra condition, if any.</summary>
    public IPredicate? OnWhen { get; } = onWhen;

    /// <summary>The clause's steps.</summary>
    public IProcessor Block { get; } = block;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Block];

    /// <summary>
    /// Whether the clause catches <paramref name="exception"/>: it or one of its inner exceptions (innermost first) is of one of
    /// the types and <see cref="OnWhen"/> matches.
    /// </summary>
    public bool Catches(Exchange exchange, Exception exception)
    {
        foreach (var e in ExceptionPolicyStrategy.CauseChainFromInnermost(exception))
        {
            foreach (var type in ExceptionTypes)
            {
                if (type.IsInstanceOfType(e))
                {
                    return OnWhen?.Matches(exchange) ?? true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange) => Block.ProcessAsync(exchange);

    /// <inheritdoc />
    public override string ToString() => $"Catch[{string.Join(", ", ExceptionTypes.Select(t => t.Name))}]";
}
