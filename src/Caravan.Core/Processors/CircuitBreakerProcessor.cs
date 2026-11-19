using Caravan.Model;
using Caravan.Spi;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Caravan.Processors;

/// <summary>
/// The <c>circuitBreaker</c> EIP (Camel's <c>Resilience4jProcessor</c>). The bulkhead (if any) admits the call, then the block runs through a Polly
/// circuit breaker and time limit, on a copy of the exchange whose result is copied back on success. When the circuit is open
/// (<see cref="BrokenCircuitException"/>), the call fails, times out (<see cref="TimeoutRejectedException"/>) or is rejected by the bulkhead
/// (<see cref="BulkheadRejectedException"/>), the fallback runs on the exchange as it was before the breaker, with the exception in
/// <see cref="ExchangePropertyKeys.ExceptionCaught"/>; without a fallback the exchange fails with that exception. The
/// <c>CaravanResponse*</c> properties tell what happened (see <see cref="ExchangePropertyKeys.ResponseSuccessfulExecution"/>).
/// </summary>
/// <remarks>
/// Timeouts count as failures of the circuit (the time limit is inside the breaker; Camel puts Resilience4j's time limiter outside it). Calls the
/// bulkhead rejects are not counted. A failure leaving the breaker is not redelivered by the route's error handler unless
/// <see cref="CircuitBreakerDefinition.InheritErrorHandler"/> is set.
/// </remarks>
public sealed class CircuitBreakerProcessor : INavigableProcessor
{
    private readonly CircuitBreakerCircuit _circuit;
    private readonly CircuitBreakerCircuit? _fallbackCircuit;
    private readonly CircuitBreakerBulkhead? _bulkhead;
    private readonly bool _inheritErrorHandler;

    internal CircuitBreakerProcessor(
        CircuitBreakerCircuit circuit,
        IProcessor block,
        IProcessor? fallback,
        CircuitBreakerCircuit? fallbackCircuit,
        CircuitBreakerConfiguration configuration,
        bool inheritErrorHandler)
    {
        _circuit = circuit;
        _fallbackCircuit = fallbackCircuit;
        _inheritErrorHandler = inheritErrorHandler;
        Block = block;
        Fallback = fallback;
        Configuration = configuration;
        if (configuration.BulkheadMaxConcurrentCalls is > 0 and var max)
        {
            _bulkhead = new CircuitBreakerBulkhead(max, Math.Max(0, configuration.BulkheadMaxWaitingCalls ?? 0));
        }
    }

    /// <summary>The node id, which names the circuit.</summary>
    public string Id => _circuit.Id;

    /// <summary>The protected block.</summary>
    public IProcessor Block { get; }

    /// <summary>The fallback block, if any.</summary>
    public IProcessor? Fallback { get; }

    /// <summary>The effective configuration.</summary>
    public CircuitBreakerConfiguration Configuration { get; }

    /// <summary>The state of the circuit.</summary>
    public CircuitBreakerState State => _circuit.State;

    /// <summary>The state of the fallback's own circuit (<c>onFallbackViaNetwork</c>), or null.</summary>
    public CircuitBreakerState? FallbackState => _fallbackCircuit?.State;

    /// <summary>The number of calls holding a bulkhead slot (0 without a bulkhead).</summary>
    public int BulkheadRunning => _bulkhead?.Running ?? 0;

    /// <summary>The number of calls waiting for a bulkhead slot (0 without a bulkhead).</summary>
    public int BulkheadWaiting => _bulkhead?.Waiting ?? 0;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => Fallback is null ? [Block] : [Block, Fallback];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        Outcome<Exchange> outcome;
        var rejected = false;
        if (_bulkhead is not null && !await _bulkhead.TryEnterAsync(exchange.CancellationToken).ConfigureAwait(false))
        {
            rejected = true;
            outcome = Outcome.FromException<Exchange>(new BulkheadRejectedException(
                $"Bulkhead of circuit breaker {Id} is full ({_bulkhead.MaxConcurrentCalls} running, {_bulkhead.MaxWaitingCalls} waiting)",
                exchange));
        }
        else
        {
            try
            {
                outcome = await _circuit.ExecuteAsync(Block, exchange).ConfigureAwait(false);
            }
            finally
            {
                _bulkhead?.Exit();
            }
        }

        if (outcome.Exception is null && outcome.Result is { Exception: null } result)
        {
            exchange.CopyResultsFrom(result);
            SetResponse(exchange, successful: true, fromFallback: false, shortCircuited: false, timedOut: false, rejected: false);
            return;
        }

        var exception = outcome.Exception ?? outcome.Result!.Exception!;
        var shortCircuited = exception is BrokenCircuitException;
        var timedOut = exception is TimeoutRejectedException;
        rejected |= shortCircuited;

        if (Fallback is null)
        {
            if (outcome.Result is { } failed)
            {
                exchange.CopyResultsFrom(failed);
            }
            else
            {
                exchange.Exception = exception;
            }

            SetResponse(exchange, successful: false, fromFallback: false, shortCircuited, timedOut, rejected);
            SuppressRedelivery(exchange);
            return;
        }

        exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, exception);
        exchange.Exception = null;
        exchange.IsRedeliveryExhausted = false;
        SetResponse(exchange, successful: false, fromFallback: true, shortCircuited, timedOut, rejected);
        await RunFallbackAsync(exchange).ConfigureAwait(false);
        if (exchange.IsFailed)
        {
            SuppressRedelivery(exchange);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"CircuitBreaker[{Id}: {Block}{(Fallback is null ? string.Empty : ", Fallback " + Fallback)}]";

    private async ValueTask RunFallbackAsync(Exchange exchange)
    {
        if (_fallbackCircuit is null)
        {
            try
            {
                await Fallback!.ProcessAsync(exchange).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            return;
        }

        var outcome = await _fallbackCircuit.ExecuteAsync(Fallback!, exchange).ConfigureAwait(false);
        if (outcome.Result is { } result)
        {
            exchange.CopyResultsFrom(result);
        }
        else
        {
            exchange.Exception = outcome.Exception;
        }
    }

    private void SetResponse(Exchange exchange, bool successful, bool fromFallback, bool shortCircuited, bool timedOut, bool rejected)
    {
        exchange.SetProperty(ExchangePropertyKeys.ResponseSuccessfulExecution, successful);
        exchange.SetProperty(ExchangePropertyKeys.ResponseFromFallback, fromFallback);
        exchange.SetProperty(ExchangePropertyKeys.ResponseShortCircuited, shortCircuited);
        exchange.SetProperty(ExchangePropertyKeys.ResponseTimedOut, timedOut);
        exchange.SetProperty(ExchangePropertyKeys.ResponseRejected, rejected);
        exchange.SetProperty(ExchangePropertyKeys.CircuitBreakerState, State.ToString());
    }

    private void SuppressRedelivery(Exchange exchange)
    {
        // The breaker handled the call: the error handler around it goes straight to onException / the dead letter channel.
        if (!_inheritErrorHandler && exchange.IsFailed)
        {
            exchange.SuppressRedelivery = true;
        }
    }
}
