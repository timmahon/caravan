using System.Collections.Concurrent;
using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan;

internal sealed class ConsumerTemplate(ICaravanContext context) : IConsumerTemplate
{
    private readonly ConcurrentDictionary<IEndpoint, Lazy<Task<IPollingConsumer>>> _consumers = new();
    private readonly ConcurrentDictionary<IEndpoint, IPollingConsumer> _started = new();

    public async Task<Exchange> ReceiveAsync(string uri, CancellationToken cancellationToken = default)
    {
        var consumer = await GetConsumerAsync(uri).ConfigureAwait(false);
        return await consumer.ReceiveAsync(Link(cancellationToken)).ConfigureAwait(false);
    }

    public async Task<Exchange?> ReceiveAsync(string uri, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var consumer = await GetConsumerAsync(uri).ConfigureAwait(false);
        return await consumer.ReceiveAsync(timeout, Link(cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask<Exchange?> ReceiveNoWaitAsync(string uri)
    {
        var consumer = await GetConsumerAsync(uri).ConfigureAwait(false);
        return consumer.ReceiveNoWait();
    }

    public async Task<object?> ReceiveBodyAsync(string uri, CancellationToken cancellationToken = default) =>
        await ExtractBodyAsync<object>(await ReceiveAsync(uri, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<object?> ReceiveBodyAsync(string uri, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        await ExtractBodyAsync<object>(await ReceiveAsync(uri, timeout, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

    public async ValueTask<object?> ReceiveBodyNoWaitAsync(string uri) =>
        await ExtractBodyAsync<object>(await ReceiveNoWaitAsync(uri).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<T?> ReceiveBodyAsync<T>(string uri, CancellationToken cancellationToken = default) =>
        await ExtractBodyAsync<T>(await ReceiveAsync(uri, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<T?> ReceiveBodyAsync<T>(string uri, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        await ExtractBodyAsync<T>(await ReceiveAsync(uri, timeout, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

    public async ValueTask<T?> ReceiveBodyNoWaitAsync<T>(string uri) =>
        await ExtractBodyAsync<T>(await ReceiveNoWaitAsync(uri).ConfigureAwait(false)).ConfigureAwait(false);

    public async ValueTask DoneUoWAsync(Exchange exchange)
    {
        var unitOfWork = exchange.UnitOfWork;
        if (unitOfWork is null && !exchange.HasPendingCompletions)
        {
            return;
        }

        unitOfWork ??= new DefaultUnitOfWork(exchange);
        await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
        exchange.UnitOfWork = null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var consumer in _started.Values)
        {
            try
            {
                await consumer.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _started.Clear();
        _consumers.Clear();
    }

    private async ValueTask<T?> ExtractBodyAsync<T>(Exchange? exchange)
    {
        if (exchange is null)
        {
            return default;
        }

        try
        {
            if (exchange.Exception is { } e)
            {
                throw new CaravanExecutionException($"Exception occurred during execution on the exchange: {e.Message}", exchange, e);
            }

            return exchange.Message.GetBody<T>();
        }
        finally
        {
            await DoneUoWAsync(exchange).ConfigureAwait(false);
        }
    }

    private CancellationToken Link(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? cancellationToken : context.ShutdownToken;

    private Task<IPollingConsumer> GetConsumerAsync(string uri) => GetConsumerAsync(context.GetEndpoint(uri));

    private Task<IPollingConsumer> GetConsumerAsync(IEndpoint endpoint) =>
        _consumers.GetOrAdd(endpoint, ep => new Lazy<Task<IPollingConsumer>>(async () =>
        {
            await ep.StartAsync().ConfigureAwait(false);
            var consumer = PollingConsumerFactory.Create(ep);
            await consumer.StartAsync().ConfigureAwait(false);
            _started[ep] = consumer;
            return consumer;
        })).Value;
}
