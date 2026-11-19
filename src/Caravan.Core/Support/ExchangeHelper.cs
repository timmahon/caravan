namespace Caravan.Support;

/// <summary>Exchange helpers shared by EIPs (Camel's <c>ExchangeHelper</c>).</summary>
internal static class ExchangeHelper
{
    /// <summary>
    /// Copies the result of <paramref name="source"/> into <paramref name="target"/>, keeping the target's exchange pattern (Camel's
    /// <c>copyResultsPreservePattern</c>): the message is replaced by a copy of the source's, the source's properties are merged in
    /// (except <see cref="ExchangePropertyKeys.CorrelationId"/>), and the failure, stop and handled state is taken over.
    /// </summary>
    public static void CopyResults(Exchange target, Exchange source)
    {
        if (ReferenceEquals(target, source))
        {
            return;
        }

        target.Message = source.Message.Copy(target);
        foreach (var (key, value) in source.Properties)
        {
            if (key != ExchangePropertyKeys.CorrelationId)
            {
                target.Properties[key] = value;
            }
        }

        target.Exception = source.Exception;
        target.IsRedeliveryExhausted = source.IsRedeliveryExhausted;
        target.IsRouteStop = source.IsRouteStop;
        target.IsRollbackOnly = source.IsRollbackOnly;
        target.IsRollbackOnlyLast = source.IsRollbackOnlyLast;
        target.ErrorHandlerHandled = source.ErrorHandlerHandled;
    }

    /// <summary>Moves the completion callbacks of <paramref name="source"/> (its unit of work's and pending ones) to <paramref name="target"/>.</summary>
    public static void HandoverCompletions(Exchange source, Exchange target)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        source.UnitOfWork?.HandoverSynchronization(target);
        foreach (var pending in source.TakePendingCompletions() ?? [])
        {
            target.AddOnCompletion(pending);
        }
    }

    /// <summary>
    /// Waits for <paramref name="delay"/> on <paramref name="timeProvider"/>: asynchronously, or by blocking the calling thread when
    /// <paramref name="asyncDelayed"/> is false (Camel's synchronous delays).
    /// </summary>
    public static ValueTask SleepAsync(TimeSpan delay, TimeProvider timeProvider, bool asyncDelayed, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        var task = Task.Delay(delay, timeProvider, cancellationToken);
        if (asyncDelayed)
        {
            return new ValueTask(task);
        }

        task.GetAwaiter().GetResult();
        return ValueTask.CompletedTask;
    }

    /// <summary>Converts a delay value: a <see cref="TimeSpan"/>, or a number of milliseconds (anything the type converter turns into a <see cref="long"/>); null is no delay.</summary>
    public static TimeSpan ToDelay(Exchange exchange, object? value) => value switch
    {
        null => TimeSpan.Zero,
        TimeSpan span => span,
        _ => TimeSpan.FromMilliseconds((long)exchange.Context.TypeConverter.Convert(typeof(long), exchange, value)!),
    };
}
