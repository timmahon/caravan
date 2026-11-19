using System.Collections.Concurrent;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Saga;

/// <summary>
/// The default <see cref="ISagaService"/> (Camel's <c>InMemorySagaService</c>): sagas live in memory, in this process. Calls to completion and
/// compensation endpoints are retried up to <see cref="MaxRetryAttempts"/> times, <see cref="RetryDelay"/> apart (on the context's clock); a
/// call that still fails is logged and the saga is left in <see cref="SagaStatus.Completing"/>/<see cref="SagaStatus.Compensating"/>. Finished
/// sagas are forgotten.
/// </summary>
public sealed class InMemorySagaService : ServiceSupport, ISagaService
{
    /// <summary>The default <see cref="MaxRetryAttempts"/>: 5 (Camel's).</summary>
    public const int DefaultMaxRetryAttempts = 5;

    /// <summary>The default <see cref="RetryDelay"/>: 5 seconds (Camel's).</summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, InMemorySagaCoordinator> _sagas = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Task> _background = new();
    private readonly ProducerCache _producers = new();
    private CancellationTokenSource _stopping = new();
    private long _sequence;

    /// <summary>Creates the service for <paramref name="context"/>.</summary>
    /// <param name="context">The context whose endpoints, clock and loggers the service uses.</param>
    public InMemorySagaService(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <summary>The context.</summary>
    public ICaravanContext Context { get; }

    /// <summary>How many times a failed call to a completion or compensation endpoint is retried (after the first attempt).</summary>
    public int MaxRetryAttempts { get; set; } = DefaultMaxRetryAttempts;

    /// <summary>The delay between attempts to call a completion or compensation endpoint.</summary>
    public TimeSpan RetryDelay { get; set; } = DefaultRetryDelay;

    /// <summary>The sagas that have not finished.</summary>
    public IReadOnlyCollection<InMemorySagaCoordinator> Sagas => [.. _sagas.Values];

    /// <inheritdoc />
    public Task<ISagaCoordinator> NewSagaAsync(Exchange exchange)
    {
        var saga = new InMemorySagaCoordinator(this, Guid.NewGuid().ToString("N"));
        _sagas[saga.Id] = saga;
        return Task.FromResult<ISagaCoordinator>(saga);
    }

    /// <inheritdoc />
    public Task<ISagaCoordinator?> GetSagaAsync(string id) =>
        Task.FromResult<ISagaCoordinator?>(_sagas.TryGetValue(id, out var saga) ? saga : null);

    /// <inheritdoc />
    public override string ToString() => $"InMemorySagaService[{_sagas.Count} saga(s)]";

    /// <summary>Forgets a finished saga.</summary>
    internal void Remove(InMemorySagaCoordinator saga) => _sagas.TryRemove(new KeyValuePair<string, InMemorySagaCoordinator>(saga.Id, saga));

    /// <summary>Runs work that no exchange waits for (compensation after a timeout); the service's stop waits for it.</summary>
    internal void RunInBackground(Func<Task> work)
    {
        var id = Interlocked.Increment(ref _sequence);
        var task = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Error in background saga work");
            }
            finally
            {
                _background.TryRemove(id, out _);
            }
        });
        _background.TryAdd(id, task);
        if (task.IsCompleted)
        {
            _background.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Calls <paramref name="endpoint"/> for a step of <paramref name="saga"/>: a new exchange with the step's option values and the saga id as
    /// headers, retried on failure. Returns whether a call succeeded.
    /// </summary>
    internal async Task<bool> SendStepAsync(InMemorySagaCoordinator saga, IEndpoint endpoint, IReadOnlyDictionary<string, object?> values, string action)
    {
        var token = _stopping.Token;
        for (var attempt = 0; ; attempt++)
        {
            var exchange = endpoint.CreateExchange(ExchangePattern.InOnly);
            foreach (var (name, value) in values)
            {
                exchange.Message.SetHeader(name, value);
            }

            exchange.Message.SetHeader(CaravanHeaders.SagaLongRunningAction, saga.Id);
            try
            {
                await _producers.SendAsync(endpoint, exchange).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            if (exchange.Exception is null)
            {
                return true;
            }

            if (attempt >= MaxRetryAttempts || token.IsCancellationRequested)
            {
                Logger.LogError(
                    exchange.Exception,
                    "Cannot {Action} saga {SagaId}: calling {Endpoint} failed after {Attempts} attempt(s)",
                    action,
                    saga.Id,
                    endpoint.EndpointUri,
                    attempt + 1);
                return false;
            }

            Logger.LogWarning(
                exchange.Exception,
                "Failed to {Action} saga {SagaId}: calling {Endpoint} failed (attempt {Attempt}); retrying in {Delay}",
                action,
                saga.Id,
                endpoint.EndpointUri,
                attempt + 1,
                RetryDelay);
            if (RetryDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(RetryDelay, Context.TimeProvider, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
    }

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (_stopping.IsCancellationRequested)
        {
            _stopping.Dispose();
            _stopping = new CancellationTokenSource();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        foreach (var saga in _sagas.Values)
        {
            saga.CancelTimeouts();
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        while (!_background.IsEmpty)
        {
            await Task.WhenAll(_background.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await _producers.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private ILogger Logger => Context.LoggerFactory.CreateLogger<InMemorySagaService>();
}
