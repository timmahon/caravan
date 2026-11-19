using Caravan.Spi;

namespace Caravan.Model;

/// <summary>A <c>doCatch</c> clause of a <c>doTry</c>: runs its steps when the exception (or one of its causes) is of one of <see cref="ExceptionTypes"/>.</summary>
public sealed class CatchDefinition : OutputDefinition
{
    /// <summary>Creates a clause catching the given exception types.</summary>
    /// <exception cref="ArgumentException">No type is given or a type is not an <see cref="Exception"/>.</exception>
    public CatchDefinition(IEnumerable<Type> exceptionTypes)
    {
        ExceptionTypes = [.. exceptionTypes];
        if (ExceptionTypes.Count == 0)
        {
            throw new ArgumentException("doCatch needs at least one exception type", nameof(exceptionTypes));
        }

        foreach (var type in ExceptionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"{type} is not an exception type", nameof(exceptionTypes));
            }
        }
    }

    /// <summary>The exception types caught (subtypes included).</summary>
    public List<Type> ExceptionTypes { get; }

    /// <summary>An extra condition for the clause to catch.</summary>
    public IPredicate? OnWhen { get; set; }

    /// <inheritdoc />
    public override string ShortName => "doCatch";

    /// <inheritdoc />
    public override string Label => $"doCatch[{string.Join(", ", ExceptionTypes.Select(t => t.Name))}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
