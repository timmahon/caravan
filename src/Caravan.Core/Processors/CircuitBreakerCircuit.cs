using Caravan.Model;
using Caravan.Spi;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Caravan.Processors;

/// <summary>
/// One circuit of a <c>circuitBreaker</c> EIP: a Polly pipeline (circuit breaker, then the time limit) on the context's clock, with its state
/// exposed for management. Calls run a processor on a copy of the exchange so a call abandoned on timeout cannot change the exchange.
/// </summary>
internal sealed class CircuitBreakerCircuit
{
    private readonly CircuitBreakerStateProvider _stateProvider = new();
    private readonly CircuitBreakerManualControl _manualControl = new();
    private readonly IList<Type>? _record;
    private readonly IList<Type>? _ignore;

    public CircuitBreakerCircuit(string id, CircuitBreakerConfiguration configuration, TimeProvider timeProvider)
    {
        Id = id;
        _record = configuration.RecordExceptions;
        _ignore = configuration.IgnoreExceptions;
        var builder = new ResiliencePipelineBuilder<Exchange> { Name = id, TimeProvider = timeProvider };
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<Exchange>
        {
            Name = id,
            FailureRatio = configuration.FailureRatio ?? CircuitBreakerConfiguration.DefaultFailureRatio,
            MinimumThroughput = configuration.MinimumThroughput ?? CircuitBreakerConfiguration.DefaultMinimumThroughput,
            SamplingDuration = configuration.SamplingDuration ?? CircuitBreakerConfiguration.DefaultSamplingDuration,
            BreakDuration = configuration.BreakDuration ?? CircuitBreakerConfiguration.DefaultBreakDuration,
            ShouldHandle = args => ValueTask.FromResult(IsRecordedFailure(args.Outcome)),
            StateProvider = _stateProvider,
            ManualControl = _manualControl,
        });
        if (configuration.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            builder.AddTimeout(timeout);
        }

        Pipeline = builder.Build();
    }

    public string Id { get; }

    public ResiliencePipeline<Exchange> Pipeline { get; }

    public CircuitBreakerState State => _stateProvider.CircuitState switch
    {
        CircuitState.Open => CircuitBreakerState.Open,
        CircuitState.HalfOpen => CircuitBreakerState.HalfOpen,
        CircuitState.Isolated => CircuitBreakerState.Isolated,
        _ => CircuitBreakerState.Closed,
    };

    public Task IsolateAsync(CancellationToken cancellationToken) => _manualControl.IsolateAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken) => _manualControl.CloseAsync(cancellationToken);

    /// <summary>
    /// Runs <paramref name="processor"/> through the circuit on a copy of <paramref name="exchange"/> (sharing its unit of work). The outcome is the
    /// processed copy (possibly failed), or an exception when the call was not made (<see cref="BrokenCircuitException"/>), timed out
    /// (<see cref="TimeoutRejectedException"/>) or was cancelled.
    /// </summary>
    public async ValueTask<Outcome<Exchange>> ExecuteAsync(IProcessor processor, Exchange exchange)
    {
        var resilienceContext = ResilienceContextPool.Shared.Get(exchange.CancellationToken);
        try
        {
            return await Pipeline.ExecuteOutcomeAsync(
                static (context, state) => RunAsync(context, state.processor, state.exchange),
                resilienceContext,
                (processor, exchange)).ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(resilienceContext);
        }
    }

    public override string ToString() => $"Circuit[{Id}: {State}]";

    private static async ValueTask<Outcome<Exchange>> RunAsync(ResilienceContext context, IProcessor processor, Exchange exchange)
    {
        var token = context.CancellationToken;
        var copy = exchange.Copy();
        copy.Exception = null;
        copy.UnitOfWork = exchange.UnitOfWork;

        // The copy's token lives as long as the call, even when it is abandoned after a timeout (Polly reuses its own token sources).
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        copy.CancellationToken = cancellation.Token;
        var call = ProcessAsync(processor, copy, cancellation);
        try
        {
            await call.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (token.IsCancellationRequested)
        {
            return Outcome.FromException<Exchange>(e);
        }

        if (token.IsCancellationRequested && copy.Exception is OperationCanceledException)
        {
            // The block gave up because the call was cancelled (e.g. timed out): report it as a cancellation, not as the block's failure.
            return Outcome.FromException<Exchange>(new OperationCanceledException(token));
        }

        return Outcome.FromResult(copy);
    }

    private static async Task ProcessAsync(IProcessor processor, Exchange copy, CancellationTokenSource cancellation)
    {
        try
        {
            await processor.ProcessAsync(copy).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            copy.Exception = e;
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private bool IsRecordedFailure(Outcome<Exchange> outcome)
    {
        var exception = outcome.Exception ?? outcome.Result?.Exception;
        if (exception is null)
        {
            return false;
        }

        if (_record is { Count: > 0 })
        {
            return Matches(_record, exception);
        }

        return _ignore is not { Count: > 0 } || !Matches(_ignore, exception);
    }

    private static bool Matches(IList<Type> types, Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            var type = e.GetType();
            if (types.Any(t => t.IsAssignableFrom(type)))
            {
                return true;
            }
        }

        return false;
    }
}
