using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>
/// Options of the redelivery error handlers (<see cref="DefaultErrorHandlerDefinition"/>, <see cref="DeadLetterChannelDefinition"/>);
/// redelivery options are in <see cref="RedeliveryDsl"/>.
/// </summary>
public static class ErrorHandlerDsl
{
    /// <summary>Called before each redelivery attempt (after the delay).</summary>
    public static T OnRedelivery<T>(this T errorHandler, IProcessor processor)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.OnRedelivery = processor;
        return errorHandler;
    }

    /// <summary>Called before each redelivery attempt (after the delay).</summary>
    public static T OnRedelivery<T>(this T errorHandler, Action<Exchange> processor)
        where T : RedeliveryErrorHandlerDefinition => errorHandler.OnRedelivery(DelegateProcessor.From(processor));

    /// <summary>Called right after each exception occurs; its failures are logged and ignored.</summary>
    public static T OnExceptionOccurred<T>(this T errorHandler, IProcessor processor)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.OnExceptionOccurred = processor;
        return errorHandler;
    }

    /// <summary>Called right after each exception occurs; its failures are logged and ignored.</summary>
    public static T OnExceptionOccurred<T>(this T errorHandler, Action<Exchange> processor)
        where T : RedeliveryErrorHandlerDefinition => errorHandler.OnExceptionOccurred(DelegateProcessor.From(processor));

    /// <summary>Prepares the exchange before it is handed to the failure processor (e.g. the dead letter endpoint).</summary>
    public static T OnPrepareFailure<T>(this T errorHandler, IProcessor processor)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.OnPrepareFailure = processor;
        return errorHandler;
    }

    /// <summary>Prepares the exchange before it is handed to the failure processor (e.g. the dead letter endpoint).</summary>
    public static T OnPrepareFailure<T>(this T errorHandler, Action<Exchange> processor)
        where T : RedeliveryErrorHandlerDefinition => errorHandler.OnPrepareFailure(DelegateProcessor.From(processor));

    /// <summary>Redelivers while the predicate matches (instead of using the maximum redeliveries).</summary>
    public static T RetryWhile<T>(this T errorHandler, IPredicate predicate)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.RetryWhile = predicate;
        return errorHandler;
    }

    /// <summary>Redelivers while the predicate matches (instead of using the maximum redeliveries).</summary>
    public static T RetryWhile<T>(this T errorHandler, Func<Exchange, bool> predicate)
        where T : RedeliveryErrorHandlerDefinition => errorHandler.RetryWhile(Pred.From(predicate));

    /// <summary>Sends the original message (as received by the route's consumer) to the dead letter endpoint.</summary>
    public static T UseOriginalMessage<T>(this T errorHandler)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.UseOriginalMessage = true;
        return errorHandler;
    }

    /// <summary>Sends the original body with the current headers to the dead letter endpoint.</summary>
    public static T UseOriginalBody<T>(this T errorHandler)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.UseOriginalBody = true;
        return errorHandler;
    }

    /// <summary>The logger category used by the error handler.</summary>
    public static T LoggerName<T>(this T errorHandler, string loggerName)
        where T : RedeliveryErrorHandlerDefinition
    {
        errorHandler.LoggerName = loggerName;
        return errorHandler;
    }

    /// <summary>Whether a new exception thrown while sending to the dead letter endpoint is handled too (default true).</summary>
    public static DeadLetterChannelDefinition DeadLetterHandleNewException(this DeadLetterChannelDefinition errorHandler, bool handleNewException)
    {
        errorHandler.HandleNewException = handleNewException;
        return errorHandler;
    }
}
