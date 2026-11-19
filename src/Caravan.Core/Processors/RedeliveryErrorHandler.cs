using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// Error handling around one node (Camel's <c>RedeliveryErrorHandler</c>, used by the default error handler and the dead letter
/// channel). When the node fails: the matching <c>onException</c> clause (if any) is chosen, the node is redelivered according to
/// the redelivery policy (with <see cref="CaravanHeaders.Redelivered"/>, <see cref="CaravanHeaders.RedeliveryCounter"/> and
/// <see cref="CaravanHeaders.RedeliveryMaxCounter"/> set and the message restored as it was before the first attempt), and once
/// redelivery is exhausted the failure processor (the clause's steps or the dead letter endpoint) runs and decides whether the
/// failure is handled, continued or propagated.
/// </summary>
public sealed class RedeliveryErrorHandler : INavigableProcessor
{
    private static readonly ExchangeFormatter Formatter = new() { ShowExchangeId = true, ShowHeaders = true, ShowBodyType = true, ShowBody = true, MaxChars = 1000 };
    private readonly RedeliveryErrorHandlerFactory _factory;
    private readonly string? _routeId;
    private readonly Management.ProcessingCounters? _routeCounters;
    private readonly Management.ProcessingCounters? _nodeCounters;

    internal RedeliveryErrorHandler(RedeliveryErrorHandlerFactory factory, ProcessorDefinition definition, IProcessor output, RouteContext? routeContext = null)
    {
        _factory = factory;
        Definition = definition;
        Output = output;
        if (routeContext is not null)
        {
            _routeId = routeContext.RouteId;
            _routeCounters = routeContext.Counters;
            if (routeContext.Context.Management is Management.DefaultManagementService { NodeStatisticsEnabled: true })
            {
                _nodeCounters = routeContext.GetProcessorCounters(definition).Counters;
            }
        }
    }

    /// <summary>The wrapped node.</summary>
    public ProcessorDefinition Definition { get; }

    /// <summary>The wrapped node's processor.</summary>
    public IProcessor Output { get; }

    /// <summary>The error handler's own redelivery policy (an <c>onException</c> clause may override it).</summary>
    public RedeliveryPolicy RedeliveryPolicy => _factory.RedeliveryPolicy;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Output];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (exchange.Exception is null)
        {
            // A failure that was dealt with earlier (caught, continued, cleared) no longer counts as exhausted.
            exchange.IsRedeliveryExhausted = false;
            exchange.SuppressRedelivery = false;
        }

        var original = _factory.RedeliveryEnabled ? exchange.Message.Copy(exchange) : null;
        var counter = 0;
        var previousDelay = TimeSpan.Zero;
        var process = true;
        while (true)
        {
            if (process)
            {
                try
                {
                    await Output.ProcessAsync(exchange).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    exchange.Exception = e;
                }
            }

            process = true;

            // Done when the node succeeded, or when an inner error handler (nested node or called route) already dealt with it.
            if (exchange.Exception is not { } exception || exchange.IsRedeliveryExhausted)
            {
                return;
            }

            exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, exception);
            var policy = _factory.FindPolicy(exchange, exception);
            var redelivery = policy?.RedeliveryPolicy ?? _factory.RedeliveryPolicy;
            var retryWhile = policy is null ? _factory.Definition.RetryWhile : policy.Definition.RetryWhile;
            counter++;

            await NotifyExceptionOccurredAsync(exchange, policy).ConfigureAwait(false);

            var suppressRedelivery = exchange.SuppressRedelivery;
            exchange.SuppressRedelivery = false;
            if (suppressRedelivery || !ShouldRedeliver(exchange, original, redelivery, counter, retryWhile))
            {
                await DeliverToFailureProcessorAsync(exchange, exception, policy, redelivery, counter).ConfigureAwait(false);
                return;
            }

            LogRetryAttempt(exchange, exception, redelivery, counter);
            var delay = redelivery.CalculateRedeliveryDelay(previousDelay, counter);
            previousDelay = delay;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, exchange.Context.TimeProvider, exchange.CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down: give up redelivering and let the failure through.
                    exchange.IsRedeliveryExhausted = true;
                    Logger(exchange).LogWarning(
                        "Redelivery of exchange {ExchangeId} cancelled after {Attempts} attempt(s) because the exchange was cancelled",
                        exchange.ExchangeId,
                        counter);
                    return;
                }
            }

            PrepareForRedelivery(exchange, original!, redelivery, counter);

            var onRedelivery = policy?.Definition.OnRedelivery ?? _factory.Definition.OnRedelivery;
            if (onRedelivery is not null)
            {
                try
                {
                    await onRedelivery.ProcessAsync(exchange).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    exchange.Exception = e;
                    process = false;
                    continue;
                }
            }

            _routeCounters?.IncrementRedeliveries();
            _nodeCounters?.IncrementRedeliveries();
            Observability.CaravanMetrics.RecordRedelivery(exchange.Context, _routeId ?? exchange.CurrentRouteId);
            EventHelper.Notify(
                exchange.Context,
                new CaravanEvent(CaravanEventType.ExchangeRedelivery, exchange.Context)
                {
                    Exchange = exchange,
                    RouteId = exchange.CurrentRouteId,
                    Attempt = counter,
                    Cause = exception,
                });
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{_factory.Definition.Name}[{Output}]";

    private static bool ShouldRedeliver(Exchange exchange, Message? original, RedeliveryPolicy redelivery, int counter, IPredicate? retryWhile)
    {
        if (original is null || exchange.IsRollbackOnly || exchange.IsRollbackOnlyLast || exchange.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return redelivery.ShouldRedeliver(exchange, counter, retryWhile);
    }

    private static void PrepareForRedelivery(Exchange exchange, Message original, RedeliveryPolicy redelivery, int counter)
    {
        exchange.Exception = null;
        exchange.IsRollbackOnly = false;
        exchange.IsRollbackOnlyLast = false;
        exchange.Message = original.Copy(exchange);
        var message = exchange.Message;
        message.SetHeader(CaravanHeaders.Redelivered, true);
        message.SetHeader(CaravanHeaders.RedeliveryCounter, counter);
        if (redelivery.MaximumRedeliveries > 0)
        {
            message.SetHeader(CaravanHeaders.RedeliveryMaxCounter, redelivery.MaximumRedeliveries);
        }
    }

    private static void RemoveRedeliveryHeaders(Exchange exchange)
    {
        exchange.Message.RemoveHeader(CaravanHeaders.Redelivered);
        exchange.Message.RemoveHeader(CaravanHeaders.RedeliveryCounter);
        exchange.Message.RemoveHeader(CaravanHeaders.RedeliveryMaxCounter);
    }

    private async ValueTask NotifyExceptionOccurredAsync(Exchange exchange, ExceptionPolicy? policy)
    {
        var processor = policy?.Definition.OnExceptionOccurred ?? _factory.Definition.OnExceptionOccurred;
        if (processor is null)
        {
            return;
        }

        var exception = exchange.Exception;
        try
        {
            await processor.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger(exchange).LogWarning(e, "Error processing onExceptionOccurred for exchange {ExchangeId}. This exception is ignored.", exchange.ExchangeId);
        }

        // The callback must not change the outcome.
        exchange.Exception = exception;
    }

    private async ValueTask DeliverToFailureProcessorAsync(Exchange exchange, Exception caught, ExceptionPolicy? policy, RedeliveryPolicy redelivery, int attempts)
    {
        if (exchange.IsSharedUnitOfWorkChild)
        {
            // A sub exchange sharing its parent's unit of work: the parent's error handler deals with the failure.
            exchange.IsRedeliveryExhausted = true;
            return;
        }

        var definition = _factory.Definition;
        var processor = policy?.Processor ?? _factory.DeadLetter;
        var isDeadLetterChannel = processor is not null && ReferenceEquals(processor, _factory.DeadLetter);

        // The failure processor must not see the exchange as failed.
        exchange.Exception = null;
        var shouldHandle = Evaluate(policy?.Definition.Handled, exchange);
        var shouldContinue = Evaluate(policy?.Definition.Continued, exchange);
        var rollbackOnly = exchange.IsRollbackOnly;
        var rollbackOnlyLast = exchange.IsRollbackOnlyLast;
        var rollback = rollbackOnly || rollbackOnlyLast;

        // Cleared so the failure processor can run; put back below when the failure is not handled.
        exchange.IsRedeliveryExhausted = false;
        exchange.IsRollbackOnly = false;
        exchange.IsRollbackOnlyLast = false;

        if (exchange.GetProperty(ExchangePropertyKeys.ToEndpoint) is { } toEndpoint)
        {
            exchange.SetProperty(ExchangePropertyKeys.FailureEndpoint, toEndpoint);
        }

        if (exchange.CurrentRouteId is { } routeId)
        {
            exchange.SetProperty(ExchangePropertyKeys.FailureRouteId, routeId);
        }

        exchange.SetProperty(ExchangePropertyKeys.FailureTimestamp, exchange.Context.TimeProvider.GetUtcNow());

        // A dead letter channel is not used to continue: continued exchanges go on in the route.
        Exception? newException = null;
        if (processor is not null && (!shouldContinue || !isDeadLetterChannel))
        {
            var useOriginalMessage = policy?.Definition.UseOriginalMessage == true || (isDeadLetterChannel && definition.UseOriginalMessage);
            var useOriginalBody = policy?.Definition.UseOriginalBody == true || (isDeadLetterChannel && definition.UseOriginalBody);
            if ((useOriginalMessage || useOriginalBody) && exchange.UnitOfWork?.OriginalMessage is { } originalMessage)
            {
                if (useOriginalMessage)
                {
                    exchange.Message = originalMessage.Copy(exchange);
                }
                else
                {
                    exchange.Message.Body = originalMessage.Body;
                }
            }

            try
            {
                if (definition.OnPrepareFailure is { } onPrepare)
                {
                    await onPrepare.ProcessAsync(exchange).ConfigureAwait(false);
                }

                if (exchange.Exception is null)
                {
                    await processor.ProcessAsync(exchange).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            newException = exchange.Exception;
            exchange.Exception = null;
            exchange.SetProperty(ExchangePropertyKeys.FailureHandled, true);
            EventHelper.Notify(
                exchange.Context,
                new CaravanEvent(CaravanEventType.ExchangeFailureHandled, exchange.Context)
                {
                    Exchange = exchange,
                    RouteId = exchange.CurrentRouteId,
                    Endpoint = isDeadLetterChannel ? ((SendProcessor)processor).Endpoint : null,
                    Cause = caught,
                });
        }

        if (newException is not null && !(isDeadLetterChannel && definition.DeadLetterHandleNewException))
        {
            // The failure processor failed: its exception fails the exchange and is not handled again.
            exchange.Exception = newException;
            exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, newException);
            exchange.IsRedeliveryExhausted = true;
            exchange.ErrorHandlerHandled = false;
            exchange.IsRollbackOnly = rollbackOnly;
            exchange.IsRollbackOnlyLast = rollbackOnlyLast;
            if (redelivery.LogNewException)
            {
                Logger(exchange).Log(
                    redelivery.RetriesExhaustedLogLevel,
                    newException,
                    "Exception occurred while trying to handle previously thrown exception on exchange {ExchangeId} using: {Processor}. The previous exception is: {PreviousException}",
                    exchange.ExchangeId,
                    processor,
                    $"{caught.GetType().Name}: {caught.Message}");
            }

            return;
        }

        if (newException is not null && redelivery.LogNewException)
        {
            Logger(exchange).LogWarning(
                newException,
                "{ErrorHandler} received new exception while sending exchange {ExchangeId} to the dead letter endpoint; the new exception is handled as deadLetterHandleNewException is enabled",
                definition.Name,
                exchange.ExchangeId);
        }

        string outcome;
        if (shouldContinue || shouldHandle || isDeadLetterChannel)
        {
            _routeCounters?.IncrementFailuresHandled();
            _nodeCounters?.IncrementFailuresHandled();
            Observability.CaravanMetrics.RecordFailureHandled(exchange.Context, _routeId ?? exchange.CurrentRouteId);
        }

        if (shouldContinue)
        {
            RemoveRedeliveryHeaders(exchange);
            outcome = "Continued by the onException clause";
        }
        else if (shouldHandle || isDeadLetterChannel)
        {
            if (isDeadLetterChannel)
            {
                RemoveRedeliveryHeaders(exchange);
            }

            exchange.ErrorHandlerHandled = true;
            exchange.SetProperty(ExchangePropertyKeys.ErrorHandled, true);
            outcome = isDeadLetterChannel
                ? $"Handled by {definition.Name}: [{definition.DeadLetterUri}]"
                : "Handled by the onException clause";
        }
        else
        {
            exchange.Exception = caught;
            exchange.IsRedeliveryExhausted = true;
            exchange.ErrorHandlerHandled = false;
            exchange.IsRollbackOnly = rollbackOnly;
            exchange.IsRollbackOnlyLast = rollbackOnlyLast;
            outcome = processor is null ? "Not handled" : $"Processed by failure processor: {processor}. Not handled";
        }

        LogExhausted(exchange, caught, redelivery, attempts, shouldHandle && !isDeadLetterChannel, shouldContinue, rollback, outcome);
    }

    private static bool Evaluate(IPredicate? predicate, Exchange exchange) => predicate is not null && predicate.Matches(exchange);

    private ILogger Logger(Exchange exchange) => exchange.Context.LoggerFactory.CreateLogger(_factory.LoggerName);

    private void LogRetryAttempt(Exchange exchange, Exception exception, RedeliveryPolicy redelivery, int attempt)
    {
        if (!redelivery.LogRetryAttempted)
        {
            return;
        }

        var logger = Logger(exchange);
        if (!logger.IsEnabled(redelivery.RetryAttemptedLogLevel))
        {
            return;
        }

        logger.Log(
            redelivery.RetryAttemptedLogLevel,
            redelivery.LogRetryStackTrace ? exception : null,
            "Failed delivery for (MessageId: {MessageId} on ExchangeId: {ExchangeId}). On delivery attempt: {Attempt} caught: {Exception}",
            exchange.Message.MessageId,
            exchange.ExchangeId,
            attempt,
            $"{exception.GetType().FullName}: {exception.Message}");
    }

    private void LogExhausted(Exchange exchange, Exception caught, RedeliveryPolicy redelivery, int attempts, bool handled, bool continued, bool rollback, string outcome)
    {
        if (!rollback && ((handled && !redelivery.LogHandled) || (continued && !redelivery.LogContinued) || !redelivery.LogExhausted))
        {
            return;
        }

        var logger = Logger(exchange);
        if (!logger.IsEnabled(redelivery.RetriesExhaustedLogLevel))
        {
            return;
        }

        var details = redelivery.LogExhaustedMessageHistory && exchange.GetProperty(ExchangePropertyKeys.MessageHistory) is not null
            ? Observability.MessageHistoryFormatter.Format(exchange, redelivery.LogExhaustedExchange, Formatter)
            : redelivery.LogExhaustedExchange ? Formatter.Format(exchange) : string.Empty;
        if (rollback)
        {
            logger.Log(
                redelivery.RetriesExhaustedLogLevel,
                redelivery.LogStackTrace ? caught : null,
                "Rollback (MessageId: {MessageId} on ExchangeId: {ExchangeId}) due: {Exception}. {Outcome} {Exchange}",
                exchange.Message.MessageId,
                exchange.ExchangeId,
                caught.Message,
                outcome,
                details);
            return;
        }

        logger.Log(
            redelivery.RetriesExhaustedLogLevel,
            redelivery.LogStackTrace ? caught : null,
            "Failed delivery for (MessageId: {MessageId} on ExchangeId: {ExchangeId}). Exhausted after delivery attempt: {Attempts} caught: {Exception}. {Outcome} {Exchange}",
            exchange.Message.MessageId,
            exchange.ExchangeId,
            attempts,
            $"{caught.GetType().FullName}: {caught.Message}",
            outcome,
            details);
    }
}
