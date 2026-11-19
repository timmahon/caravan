using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>split</c> EIP.</summary>
public static class SplitDsl
{
    /// <summary>
    /// Splits the value of <paramref name="expression"/> (a sequence, array, async sequence or tokenizer such as
    /// <see cref="Expr.Tokenize(string, bool, int, bool)"/>) and runs <paramref name="body"/> for each part, on a copy of the exchange
    /// whose body is the part: <c>.Split(Expr.Tokenize(","), s =&gt; s.To("mock:line"))</c>. By default the exchange continues unchanged
    /// after the split. Part properties: <see cref="ExchangePropertyKeys.SplitIndex"/>, <see cref="ExchangePropertyKeys.SplitSize"/>,
    /// <see cref="ExchangePropertyKeys.SplitComplete"/>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the parts.</param>
    /// <param name="body">The steps run for each part.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the original exchange, failing if a part failed (<see cref="AggregationStrategies.UseOriginal"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the parts concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of parts processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">Split lazily, processing each part as it is read (the size is only known on the last part); with parallel processing, also aggregate in completion order.</param>
    /// <param name="stopOnException">Stop at the first failed part: no further part is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished parts are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each part exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Parts share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the part's own redeliveries).</param>
    /// <param name="delimiter">When set, a string value is split on it (parts trimmed, empty parts skipped); by default a string is a single part.</param>
    public static T Split<T>(
        this T steps,
        IExpression expression,
        Action<RouteSteps> body,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = null)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new SplitDefinition(expression) { Delimiter = delimiter });
        FanOutOptions.Apply(definition, aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork);
        steps.BuildNested(definition.Outputs, definition, body);
        return steps;
    }

    /// <summary>Splits the value returned by <paramref name="expression"/>: <c>.Split(e =&gt; e.Message.GetBody&lt;Order&gt;()!.Lines, s =&gt; ...)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the parts.</param>
    /// <param name="body">The steps run for each part.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the original exchange, failing if a part failed (<see cref="AggregationStrategies.UseOriginal"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the parts concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of parts processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">Split lazily, processing each part as it is read (the size is only known on the last part); with parallel processing, also aggregate in completion order.</param>
    /// <param name="stopOnException">Stop at the first failed part: no further part is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished parts are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each part exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Parts share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the part's own redeliveries).</param>
    /// <param name="delimiter">When set, a string value is split on it (parts trimmed, empty parts skipped); by default a string is a single part.</param>
    public static T Split<T>(
        this T steps,
        Func<Exchange, object?> expression,
        Action<RouteSteps> body,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = null)
        where T : RouteSteps
        => steps.Split(new FuncExpression(expression), body, aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork, delimiter);

    /// <summary>Splits a value computed from the body converted to <typeparamref name="TBody"/>: <c>.Split((Order o) =&gt; o.Lines, s =&gt; ...)</c>.</summary>
    /// <typeparam name="T">The builder type.</typeparam>
    /// <typeparam name="TBody">The type the body is converted to.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the parts from the body.</param>
    /// <param name="body">The steps run for each part.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the original exchange, failing if a part failed (<see cref="AggregationStrategies.UseOriginal"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the parts concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of parts processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">Split lazily, processing each part as it is read (the size is only known on the last part); with parallel processing, also aggregate in completion order.</param>
    /// <param name="stopOnException">Stop at the first failed part: no further part is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished parts are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each part exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Parts share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the part's own redeliveries).</param>
    /// <param name="delimiter">When set, a string value is split on it (parts trimmed, empty parts skipped); by default a string is a single part.</param>
    public static T Split<T, TBody>(
        this T steps,
        Func<TBody, object?> expression,
        Action<RouteSteps> body,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = null)
        where T : RouteSteps
        => steps.Split(Expr.Body(expression), body, aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork, delimiter);
}
