namespace Caravan.Spi;

/// <summary>
/// The unit of work in a route. Processors may throw; the route channel records the exception
/// on <see cref="Exchange.Exception"/> and hands it to the error handler.
/// </summary>
public interface IProcessor
{
    ValueTask ProcessAsync(Exchange exchange);
}

/// <summary>A processor that wraps other processors (pipeline, choice, filter, ...).</summary>
public interface INavigableProcessor : IProcessor
{
    IReadOnlyList<IProcessor> Next { get; }
}

/// <summary>A processor created from a lambda.</summary>
public sealed class DelegateProcessor(Func<Exchange, ValueTask> action) : IProcessor
{
    public static DelegateProcessor From(Action<Exchange> action) =>
        new(e =>
        {
            action(e);
            return ValueTask.CompletedTask;
        });

    public static DelegateProcessor From(Func<Exchange, Task> action) => new(e => new ValueTask(action(e)));

    public ValueTask ProcessAsync(Exchange exchange) => action(exchange);
}
