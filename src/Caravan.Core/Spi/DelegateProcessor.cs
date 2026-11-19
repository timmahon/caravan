namespace Caravan.Spi;

/// <summary>A processor created from a lambda.</summary>
public sealed class DelegateProcessor(Func<Exchange, ValueTask> action) : IProcessor
{
    /// <summary>Creates a processor from a synchronous lambda.</summary>
    public static DelegateProcessor From(Action<Exchange> action) =>
        new(e =>
        {
            action(e);
            return ValueTask.CompletedTask;
        });

    /// <summary>Creates a processor from an asynchronous lambda.</summary>
    public static DelegateProcessor From(Func<Exchange, Task> action) => new(e => new ValueTask(action(e)));

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange) => action(exchange);
}
