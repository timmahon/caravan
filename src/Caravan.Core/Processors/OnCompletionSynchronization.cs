using Caravan.Model;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// Runs an <c>onCompletion</c> block for one exchange. Equal to any other instance for the same definition, so the block is registered
/// once per unit of work.
/// </summary>
internal sealed class OnCompletionSynchronization(OnCompletionDefinition definition, IProcessor block) : ISynchronization
{
    public OnCompletionDefinition Definition { get; } = definition;

    /// <summary>Before-consumer blocks run in the route that registered them, so they stay with the exchange.</summary>
    public bool AllowHandover => Definition.Mode == OnCompletionMode.AfterConsumer;

    public ValueTask OnCompleteAsync(Exchange exchange) =>
        Definition.Mode == OnCompletionMode.AfterConsumer ? RunAfterConsumerAsync(exchange, failed: false) : ValueTask.CompletedTask;

    public ValueTask OnFailureAsync(Exchange exchange) =>
        Definition.Mode == OnCompletionMode.AfterConsumer ? RunAfterConsumerAsync(exchange, failed: true) : ValueTask.CompletedTask;

    /// <summary>Runs the block on the exchange itself (or a copy when parallel), keeping its routing state.</summary>
    public async ValueTask RunBeforeConsumerAsync(Exchange exchange)
    {
        var failed = exchange.IsFailed || exchange.IsRollbackOnly || exchange.IsRollbackOnlyLast;
        if (!ShouldRun(failed))
        {
            return;
        }

        if (Definition.ParallelProcessing)
        {
            await RunOnCopyAsync(exchange).ConfigureAwait(false);
            return;
        }

        // Clear what would stop the block (or make it look failed) and put it back afterwards, as Camel does.
        var exception = exchange.Exception;
        var stop = exchange.IsRouteStop;
        var rollbackOnly = exchange.IsRollbackOnly;
        var rollbackOnlyLast = exchange.IsRollbackOnlyLast;
        var handled = exchange.ErrorHandlerHandled;
        var exhausted = exchange.IsRedeliveryExhausted;
        var message = Definition.UseOriginalMessage ? exchange.Message : null;
        exchange.Exception = null;
        exchange.IsRouteStop = false;
        exchange.IsRollbackOnly = false;
        exchange.IsRollbackOnlyLast = false;
        exchange.ErrorHandlerHandled = false;
        exchange.IsRedeliveryExhausted = false;
        if (exception is not null)
        {
            exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, exception);
        }

        if (message is not null && exchange.UnitOfWork?.OriginalMessage is { } original)
        {
            exchange.Message = original.Copy(exchange);
        }

        exchange.SetProperty(ExchangePropertyKeys.OnCompletion, true);
        try
        {
            if (Definition.OnWhen?.Matches(exchange) ?? true)
            {
                await ProcessAsync(exchange).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            LogFailure(exchange, e);
        }
        finally
        {
            exchange.RemoveProperty(ExchangePropertyKeys.OnCompletion);
            if (message is not null)
            {
                exchange.Message = message;
            }

            exchange.Exception = exception;
            exchange.IsRouteStop = stop;
            exchange.IsRollbackOnly = rollbackOnly;
            exchange.IsRollbackOnlyLast = rollbackOnlyLast;
            exchange.ErrorHandlerHandled = handled;
            exchange.IsRedeliveryExhausted = exhausted;
        }
    }

    public override bool Equals(object? obj) => obj is OnCompletionSynchronization other && ReferenceEquals(other.Definition, Definition);

    public override int GetHashCode() => Definition.GetHashCode();

    private bool ShouldRun(bool failed) => failed ? !Definition.OnCompleteOnly : !Definition.OnFailureOnly;

    private ValueTask RunAfterConsumerAsync(Exchange exchange, bool failed) => ShouldRun(failed) ? RunOnCopyAsync(exchange) : ValueTask.CompletedTask;

    private async ValueTask RunOnCopyAsync(Exchange exchange)
    {
        var copy = exchange.CreateCorrelatedCopy();
        copy.Pattern = ExchangePattern.InOnly;
        if (exchange.Exception is { } exception)
        {
            copy.SetProperty(ExchangePropertyKeys.ExceptionCaught, exception);
        }

        if (Definition.UseOriginalMessage && exchange.UnitOfWork?.OriginalMessage is { } original)
        {
            copy.Message = original.Copy(copy);
        }

        copy.SetProperty(ExchangePropertyKeys.OnCompletion, true);
        if (Definition.ParallelProcessing)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunIfMatchesAsync(copy).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    LogFailure(copy, e);
                }
            });
            return;
        }

        try
        {
            await RunIfMatchesAsync(copy).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogFailure(copy, e);
        }
    }

    private async ValueTask RunIfMatchesAsync(Exchange exchange)
    {
        if (Definition.OnWhen?.Matches(exchange) ?? true)
        {
            await ProcessAsync(exchange).ConfigureAwait(false);
        }
    }

    private async ValueTask ProcessAsync(Exchange exchange)
    {
        await block.ProcessAsync(exchange).ConfigureAwait(false);
        if (exchange.Exception is { } e)
        {
            LogFailure(exchange, e);
        }
    }

    private void LogFailure(Exchange exchange, Exception e) =>
        exchange.Context.LoggerFactory.CreateLogger<OnCompletionProcessor>()
            .LogWarning(e, "Error processing onCompletion {OnCompletionId} for exchange {ExchangeId}. This exception is ignored.", Definition.Id, exchange.ExchangeId);
}
