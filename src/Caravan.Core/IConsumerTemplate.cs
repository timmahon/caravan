namespace Caravan;

/// <summary>
/// Receives messages from endpoints in application code (Camel's <c>ConsumerTemplate</c>), using each endpoint's
/// polling consumer (cached per endpoint and started on first use; disposing the template stops them).
/// <code>
/// await using var consumer = context.CreateConsumerTemplate();
/// var body = await consumer.ReceiveBodyAsync&lt;string&gt;("seda:orders", TimeSpan.FromSeconds(5));
/// </code>
/// <c>Receive*</c> methods returning an <see cref="Exchange"/> leave its unit of work open: call
/// <see cref="DoneUoWAsync"/> when done with it (this runs its completion callbacks, e.g. replies a waiting
/// <c>seda</c> producer). <c>ReceiveBody*</c> methods do that themselves.
/// </summary>
public interface IConsumerTemplate : IAsyncDisposable
{
    /// <summary>Waits until a message is available.</summary>
    Task<Exchange> ReceiveAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Waits up to <paramref name="timeout"/> for a message; null when none arrived.</summary>
    Task<Exchange?> ReceiveAsync(string uri, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a message if one is available right away, otherwise null. Asynchronous only because the endpoint's
    /// polling consumer is started on first use.
    /// </summary>
    ValueTask<Exchange?> ReceiveNoWaitAsync(string uri);

    /// <summary>Waits for a message and returns its body.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    Task<object?> ReceiveBodyAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Waits up to <paramref name="timeout"/> for a message and returns its body; null when none arrived.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    Task<object?> ReceiveBodyAsync(string uri, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Returns the body of a message available right away, otherwise null.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    ValueTask<object?> ReceiveBodyNoWaitAsync(string uri);

    /// <summary>Waits for a message and returns its body converted to <typeparamref name="T"/>.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    Task<T?> ReceiveBodyAsync<T>(string uri, CancellationToken cancellationToken = default);

    /// <summary>Waits up to <paramref name="timeout"/> for a message and returns its body converted to <typeparamref name="T"/>; default when none arrived.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    Task<T?> ReceiveBodyAsync<T>(string uri, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Returns the body, converted to <typeparamref name="T"/>, of a message available right away; otherwise default.</summary>
    /// <exception cref="CaravanExecutionException">The received exchange failed.</exception>
    ValueTask<T?> ReceiveBodyNoWaitAsync<T>(string uri);

    /// <summary>Completes the unit of work of a received exchange, running its completion callbacks.</summary>
    ValueTask DoneUoWAsync(Exchange exchange);
}
