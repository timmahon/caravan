using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Mock;

/// <summary>
/// A mock endpoint. Set expectations, run the test, then call <see cref="AssertIsSatisfiedAsync(TimeSpan?)"/>:
/// <code>
/// var mock = context.GetMockEndpoint("mock:result");
/// mock.ExpectedBodiesReceived("Hello");
/// await template.SendBodyAsync("direct:start", "Hello");
/// await mock.AssertIsSatisfiedAsync();
/// </code>
/// </summary>
public sealed class MockEndpoint(string uri, MockComponent component, string name)
    : EndpointBase(uri, component, component.Context!)
{
    private readonly object _lock = new();
    private readonly List<Exchange> _received = [];
    private readonly List<Func<MockEndpoint, string?>> _expectations = [];
    private readonly Dictionary<int, IProcessor> _processorsByIndex = [];
    private readonly List<Action<Exchange>> _replyActions = [];
    private TaskCompletionSource _countReached = NewTcs();
    private int _receivedCounter;
    private int _expectedCount = -1;
    private int _expectedMinimumCount = -1;
    private IProcessor? _defaultProcessor;
    private Exception? _failure;

    /// <summary>The mock name (the URI path).</summary>
    public string Name { get; } = name;

    /// <summary>Only retain the first N exchanges (-1 = all).</summary>
    public int RetainFirst { get; set; } = -1;

    /// <summary>Only retain the last N exchanges (-1 = all).</summary>
    public int RetainLast { get; set; } = -1;

    /// <summary>Maximum time <see cref="AssertIsSatisfiedAsync(TimeSpan?)"/> waits for the expected messages (default 10s).</summary>
    public TimeSpan ResultWaitTime { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// After the expected count is reached, wait this long and re-check, to detect extra messages.
    /// Also used as the wait time when expecting zero messages.
    /// </summary>
    public TimeSpan AssertPeriod { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// The minimum time <see cref="AssertIsSatisfiedAsync(TimeSpan?)"/> must wait for the expected messages (Camel's
    /// <c>resultMinimumWaitTime</c>): the assertion fails when they arrive sooner (counted from the call). Zero (default) disables the check.
    /// </summary>
    public TimeSpan ResultMinimumWaitTime { get; set; } = TimeSpan.Zero;

    /// <summary>Simulated processing delay per message.</summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>The number of exchanges received, including those not retained.</summary>
    public int ReceivedCounter => Volatile.Read(ref _receivedCounter);

    /// <summary>The retained exchanges (copies of what was received).</summary>
    public IReadOnlyList<Exchange> ReceivedExchanges
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>The bodies of the retained exchanges, in arrival order.</summary>
    public IReadOnlyList<object?> ReceivedBodies => ReceivedExchanges.Select(e => e.Message.Body).ToList();

    /// <summary>The bodies of the retained exchanges converted to <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T?> GetReceivedBodies<T>() => ReceivedExchanges.Select(e => e.Message.GetBody<T>()).ToList();

    /// <inheritdoc />
    public override bool IsLenientProperties => false;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new MockProducer(this);

    /// <summary>Creates a consumer that never produces messages (a mock is only sent to).</summary>
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The mock component does not support consumers");

    // ----- Expectations -----

    /// <summary>Expects exactly <paramref name="count"/> messages.</summary>
    public MockEndpoint ExpectedMessageCount(int count)
    {
        _expectedCount = count;
        if (count == 0 && AssertPeriod == TimeSpan.Zero)
        {
            AssertPeriod = TimeSpan.FromMilliseconds(500);
        }

        CheckCountReached();
        return this;
    }

    /// <summary>Expects at least <paramref name="count"/> messages.</summary>
    public MockEndpoint ExpectedMinimumMessageCount(int count)
    {
        _expectedMinimumCount = count;
        CheckCountReached();
        return this;
    }

    /// <summary>Expects exactly these bodies, in order.</summary>
    public MockEndpoint ExpectedBodiesReceived(params object?[] bodies)
    {
        ExpectedMessageCount(bodies.Length);
        Expect(m =>
        {
            var received = m.ReceivedExchanges;
            for (var i = 0; i < bodies.Length && i < received.Count; i++)
            {
                if (!BodyEquals(received[i], bodies[i]))
                {
                    return $"Message {i}: expected body <{bodies[i]}> but was <{received[i].Message.Body}>";
                }
            }

            return null;
        });
        return this;
    }

    /// <summary>Expects exactly these bodies, in any order.</summary>
    public MockEndpoint ExpectedBodiesReceivedInAnyOrder(params object?[] bodies)
    {
        ExpectedMessageCount(bodies.Length);
        Expect(m =>
        {
            var remaining = m.ReceivedExchanges.ToList();
            foreach (var body in bodies)
            {
                var match = remaining.FirstOrDefault(e => BodyEquals(e, body));
                if (match is null)
                {
                    return $"Expected body <{body}> was not received. Received: [{string.Join(", ", m.ReceivedBodies)}]";
                }

                remaining.Remove(match);
            }

            return null;
        });
        return this;
    }

    /// <summary>Expects the header value on every received message.</summary>
    public MockEndpoint ExpectedHeaderReceived(string name, object? value)
    {
        Expect(m =>
        {
            foreach (var (exchange, i) in m.ReceivedExchanges.Select((e, i) => (e, i)))
            {
                var actual = exchange.Message.GetHeader(name);
                if (!ValueEquals(exchange, actual, value))
                {
                    return $"Message {i}: expected header {name}=<{value}> but was <{actual}>";
                }
            }

            return m.ReceivedCounter == 0 ? $"Expected header {name}=<{value}> but no messages were received" : null;
        });
        return this;
    }

    /// <summary>Expects these values for a header, one per message, in order.</summary>
    public MockEndpoint ExpectedHeaderValuesReceivedInAnyOrder(string name, params object?[] values)
    {
        ExpectedMessageCount(values.Length);
        Expect(m =>
        {
            var remaining = m.ReceivedExchanges.Select(e => e.Message.GetHeader(name)).ToList();
            foreach (var value in values)
            {
                var index = remaining.FindIndex(v => Equals(v?.ToString(), value?.ToString()));
                if (index < 0)
                {
                    return $"Expected header {name} value <{value}> was not received";
                }

                remaining.RemoveAt(index);
            }

            return null;
        });
        return this;
    }

    /// <summary>Expects the exchange property value on every received message.</summary>
    public MockEndpoint ExpectedPropertyReceived(string name, object? value)
    {
        Expect(m =>
        {
            foreach (var (exchange, i) in m.ReceivedExchanges.Select((e, i) => (e, i)))
            {
                var actual = exchange.GetProperty(name);
                if (!ValueEquals(exchange, actual, value))
                {
                    return $"Message {i}: expected property {name}=<{value}> but was <{actual}>";
                }
            }

            return null;
        });
        return this;
    }

    /// <summary>Expects every message to match the predicate.</summary>
    public MockEndpoint AllMessages(Func<Exchange, bool> predicate, string description = "predicate")
    {
        Expect(m =>
        {
            foreach (var (exchange, i) in m.ReceivedExchanges.Select((e, i) => (e, i)))
            {
                if (!predicate(exchange))
                {
                    return $"Message {i} did not match {description}";
                }
            }

            return null;
        });
        return this;
    }

    /// <summary>Expects message <paramref name="index"/> to match the predicate.</summary>
    public MockEndpoint Message(int index, Func<Exchange, bool> predicate, string description = "predicate")
    {
        Expect(m =>
        {
            var received = m.ReceivedExchanges;
            if (index >= received.Count)
            {
                return $"Message {index} was not received (received {received.Count})";
            }

            return predicate(received[index]) ? null : $"Message {index} did not match {description}";
        });
        return this;
    }

    /// <summary>
    /// Fluent expectations on message <paramref name="index"/> (0-based):
    /// <c>mock.Message(0).Header("type").IsEqualTo("order").Body&lt;int&gt;().IsGreaterThan(0)</c>.
    /// </summary>
    /// <param name="index">The message index.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause Message(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new MockAssertionClause(this, index);
    }

    /// <summary>Fluent expectations every received message must meet: <c>mock.AllMessages().Header("tenant").IsNotNull()</c>.</summary>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause AllMessages() => new(this, null);

    /// <summary>Fluent expectations on the body of every received message: <c>mock.ExpectedBodyReceived().IsEqualTo("Hello")</c>.</summary>
    /// <returns>The value clause.</returns>
    public MockValueClause ExpectedBodyReceived() => AllMessages().Body();

    /// <summary>Expects exactly as many messages as predicates, message <c>i</c> matching predicate <c>i</c>.</summary>
    /// <param name="predicates">One predicate per expected message.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectedMessagesMatches(params IPredicate[] predicates)
    {
        ExpectedMessageCount(predicates.Length);
        for (var i = 0; i < predicates.Length; i++)
        {
            Message(i).Matches(predicates[i]);
        }

        return this;
    }

    /// <summary>Expects exactly as many messages as predicates, message <c>i</c> matching predicate <c>i</c>.</summary>
    /// <param name="predicates">One predicate per expected message.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectedMessagesMatches(params Func<Exchange, bool>[] predicates) =>
        ExpectedMessagesMatches([.. predicates.Select((p, i) => Pred.From(p, $"predicate {i}"))]);

    /// <summary>Expects the values of <paramref name="expression"/> over the received messages to be in ascending order (equal values allowed).</summary>
    /// <param name="expression">Computes the value to order by.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsAscending(IExpression expression) => ExpectsSorted(expression.Evaluate, expression.ToString(), ascending: true);

    /// <summary>Expects the values of <paramref name="expression"/> over the received messages to be in ascending order (equal values allowed).</summary>
    /// <param name="expression">Computes the value to order by.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsAscending(Func<Exchange, object?> expression) => ExpectsSorted(expression, "expression", ascending: true);

    /// <summary>Expects the values of <paramref name="expression"/> over the received messages to be in descending order (equal values allowed).</summary>
    /// <param name="expression">Computes the value to order by.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsDescending(IExpression expression) => ExpectsSorted(expression.Evaluate, expression.ToString(), ascending: false);

    /// <summary>Expects the values of <paramref name="expression"/> over the received messages to be in descending order (equal values allowed).</summary>
    /// <param name="expression">Computes the value to order by.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsDescending(Func<Exchange, object?> expression) => ExpectsSorted(expression, "expression", ascending: false);

    /// <summary>Expects no two received messages to have the same value of <paramref name="expression"/>.</summary>
    /// <param name="expression">Computes the key.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsNoDuplicates(IExpression expression) => ExpectsNoDuplicates(expression.Evaluate, expression.ToString());

    /// <summary>Expects no two received messages to have the same value of <paramref name="expression"/>.</summary>
    /// <param name="expression">Computes the key.</param>
    /// <param name="description">Describes the key in failure messages.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsNoDuplicates(Func<Exchange, object?> expression, string? description = null) =>
        Expect(m =>
        {
            var seen = new Dictionary<object, int>();
            var received = m.ReceivedExchanges;
            for (var i = 0; i < received.Count; i++)
            {
                var key = expression(received[i]);
                if (key is null)
                {
                    continue;
                }

                if (seen.TryGetValue(key, out var first))
                {
                    return $"Duplicate message found on message {i} (same as message {first}) with {description ?? "key"} <{key}>";
                }

                seen[key] = i;
            }

            return null;
        });

    /// <summary>Expects no two received messages to have the same body.</summary>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectsNoDuplicateBodies() => ExpectsNoDuplicates(e => e.Message.Body, "body");

    /// <summary>Expects every received message to have been sent with <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The expected exchange pattern.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectedExchangePattern(ExchangePattern pattern) =>
        AllMessages(e => e.Pattern == pattern, $"exchange pattern {pattern}");

    /// <summary>Expects these values for an exchange property, one per message, in any order.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="values">The expected values.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ExpectedPropertyValuesReceivedInAnyOrder(string name, params object?[] values)
    {
        ExpectedMessageCount(values.Length);
        Expect(m =>
        {
            var remaining = m.ReceivedExchanges.ToList();
            foreach (var value in values)
            {
                var index = remaining.FindIndex(e => ValueEquals(e, e.GetProperty(name), value));
                if (index < 0)
                {
                    return $"Expected property {name} value <{value}> was not received. Received: " +
                        $"[{string.Join(", ", m.ReceivedExchanges.Select(e => e.GetProperty(name)))}]";
                }

                remaining.RemoveAt(index);
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Adds an assertion run when the mock is asserted (Camel's <c>expects(Runnable)</c>): it fails the assertion by throwing, e.g. with your
    /// test framework's asserts.
    /// </summary>
    /// <param name="assertion">The assertion.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint Expects(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        return Expect(_ =>
        {
            try
            {
                assertion();
                return null;
            }
            catch (Exception e)
            {
                return $"Assertion failed: {e.Message}";
            }
        });
    }

    /// <summary>Adds a custom expectation: return null when satisfied or an error message.</summary>
    public MockEndpoint Expect(Func<MockEndpoint, string?> expectation)
    {
        lock (_lock)
        {
            _expectations.Add(expectation);
        }

        return this;
    }

    // ----- Behaviour -----

    /// <summary>Runs a processor for every received exchange (e.g. to set a reply or throw).</summary>
    public MockEndpoint WhenAnyExchangeReceived(Action<Exchange> processor)
    {
        _defaultProcessor = DelegateProcessor.From(processor);
        return this;
    }

    /// <summary>Runs <paramref name="processor"/> for every received exchange (e.g. to set a reply).</summary>
    public MockEndpoint WhenAnyExchangeReceived(IProcessor processor)
    {
        _defaultProcessor = processor;
        return this;
    }

    /// <summary>Runs a processor for the exchange with the given 0-based index.</summary>
    public MockEndpoint WhenExchangeReceived(int index, Action<Exchange> processor)
    {
        _processorsByIndex[index] = DelegateProcessor.From(processor);
        return this;
    }

    /// <summary>Sets the reply body of every exchange (after the processors set with <see cref="WhenAnyExchangeReceived(Action{Exchange})"/>).</summary>
    public MockEndpoint ReturnReplyBody(Func<Exchange, object?> body)
    {
        lock (_lock)
        {
            _replyActions.Add(e => e.Message.Body = body(e));
        }

        return this;
    }

    /// <summary>Sets the reply body of every exchange to the value of <paramref name="body"/>.</summary>
    /// <param name="body">Computes the reply body.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ReturnReplyBody(IExpression body) => ReturnReplyBody(body.Evaluate);

    /// <summary>Sets a reply header on every exchange (after the processors set with <see cref="WhenAnyExchangeReceived(Action{Exchange})"/>).</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">Computes the header value.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ReturnReplyHeader(string name, Func<Exchange, object?> value)
    {
        lock (_lock)
        {
            _replyActions.Add(e => e.Message.SetHeader(name, value(e)));
        }

        return this;
    }

    /// <summary>Sets a reply header on every exchange to the value of <paramref name="value"/>.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">Computes the header value.</param>
    /// <returns>This endpoint.</returns>
    public MockEndpoint ReturnReplyHeader(string name, IExpression value) => ReturnReplyHeader(name, value.Evaluate);

    /// <summary>The retained exchanges (Camel's <c>getExchanges()</c>).</summary>
    /// <returns>Copies of the received exchanges, in arrival order.</returns>
    public IReadOnlyList<Exchange> GetExchanges() => ReceivedExchanges;

    /// <summary>Returns the retained exchange at <paramref name="index"/> (0-based), failing when it was not received.</summary>
    /// <param name="index">The message index.</param>
    /// <returns>A copy of the received exchange.</returns>
    /// <exception cref="MockAssertionException">Fewer messages were received.</exception>
    public Exchange AssertExchangeReceived(int index)
    {
        var received = ReceivedExchanges;
        if (index < 0 || index >= received.Count)
        {
            throw new MockAssertionException($"{EndpointUri}: expected to have received exchange {index} but only {received.Count} were received");
        }

        return received[index];
    }

    // ----- Assertions -----

    /// <summary>Waits until the expected message count is reached (or the timeout elapses) and checks all expectations.</summary>
    /// <exception cref="MockAssertionException">An expectation is not met.</exception>
    public async Task AssertIsSatisfiedAsync(TimeSpan? timeout = null)
    {
        var error = await WaitAndCheckAsync(timeout).ConfigureAwait(false);
        if (error is not null)
        {
            throw new MockAssertionException($"{EndpointUri}: {error}");
        }
    }

    /// <summary>Asserts that the expectations are not met within the timeout.</summary>
    /// <exception cref="MockAssertionException">All expectations are met.</exception>
    public async Task AssertIsNotSatisfiedAsync(TimeSpan? timeout = null)
    {
        var error = await WaitAndCheckAsync(timeout ?? TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        if (error is null)
        {
            throw new MockAssertionException($"{EndpointUri}: expected the expectations not to be satisfied");
        }
    }

    /// <summary>Asserts that all mock endpoints of the context are satisfied.</summary>
    public static async Task AssertIsSatisfiedAsync(ICaravanContext context, TimeSpan? timeout = null)
    {
        foreach (var mock in context.Endpoints.OfType<MockEndpoint>())
        {
            await mock.AssertIsSatisfiedAsync(timeout).ConfigureAwait(false);
        }
    }

    /// <summary>Resets all mock endpoints of the context.</summary>
    public static void ResetMocks(ICaravanContext context)
    {
        foreach (var mock in context.Endpoints.OfType<MockEndpoint>())
        {
            mock.Reset();
        }
    }

    /// <summary>Clears received exchanges, expectations and processors.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _received.Clear();
            _expectations.Clear();
            _processorsByIndex.Clear();
            _defaultProcessor = null;
            _replyActions.Clear();
            _receivedCounter = 0;
            _expectedCount = -1;
            _expectedMinimumCount = -1;
            _failure = null;
            _countReached = NewTcs();
            AssertPeriod = TimeSpan.Zero;
        }
    }

    private async Task<string?> WaitAndCheckAsync(TimeSpan? timeout)
    {
        var wait = timeout ?? ResultWaitTime;
        if (_expectedCount > 0 || _expectedMinimumCount > 0)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                Task countReached;
                lock (_lock)
                {
                    countReached = _countReached.Task;
                }

                await countReached.WaitAsync(wait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // fall through to the checks
            }

            var waited = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (ResultMinimumWaitTime > TimeSpan.Zero && waited < ResultMinimumWaitTime)
            {
                return $"Expected a minimum of {ResultMinimumWaitTime.TotalMilliseconds} ms waiting on the result, but was faster with " +
                    $"{waited.TotalMilliseconds:0.###} ms";
            }
        }

        if (AssertPeriod > TimeSpan.Zero)
        {
            await Task.Delay(AssertPeriod).ConfigureAwait(false);
        }

        if (_failure is not null)
        {
            return $"Failure while processing: {_failure.Message}";
        }

        var count = ReceivedCounter;
        if (_expectedCount >= 0 && count != _expectedCount)
        {
            return $"Received message count. Expected: <{_expectedCount}> but was: <{count}>";
        }

        if (_expectedMinimumCount >= 0 && count < _expectedMinimumCount)
        {
            return $"Received message count. Expected minimum: <{_expectedMinimumCount}> but was: <{count}>";
        }

        List<Func<MockEndpoint, string?>> expectations;
        lock (_lock)
        {
            expectations = [.. _expectations];
        }

        return expectations.Select(e => e(this)).FirstOrDefault(r => r is not null);
    }

    internal async ValueTask OnExchangeAsync(Exchange exchange)
    {
        int index;
        lock (_lock)
        {
            index = _receivedCounter;
        }

        if (Delay is { } delay)
        {
            await Task.Delay(delay, Context.TimeProvider).ConfigureAwait(false);
        }

        try
        {
            var processor = _processorsByIndex.GetValueOrDefault(index) ?? _defaultProcessor;
            if (processor is not null)
            {
                await processor.ProcessAsync(exchange).ConfigureAwait(false);
            }

            Action<Exchange>[] replies;
            lock (_lock)
            {
                replies = [.. _replyActions];
            }

            foreach (var reply in replies)
            {
                reply(exchange);
            }
        }
        finally
        {
            lock (_lock)
            {
                var copy = exchange.Copy();
                var keep = RetainFirst < 0 || _received.Count < RetainFirst;
                if (keep)
                {
                    _received.Add(copy);
                    if (RetainLast >= 0 && RetainFirst < 0 && _received.Count > RetainLast)
                    {
                        _received.RemoveAt(0);
                    }
                }

                _receivedCounter++;
            }

            CheckCountReached();
        }
    }

    private void CheckCountReached()
    {
        lock (_lock)
        {
            var count = _receivedCounter;
            if ((_expectedCount > 0 && count >= _expectedCount) || (_expectedMinimumCount > 0 && count >= _expectedMinimumCount))
            {
                _countReached.TrySetResult();
            }
            else if (_countReached.Task.IsCompleted)
            {
                // The expectation was raised after an earlier one was met: wait for the new count.
                _countReached = NewTcs();
            }
        }
    }

    private MockEndpoint ExpectsSorted(Func<Exchange, object?> expression, string? description, bool ascending) =>
        Expect(m =>
        {
            var received = m.ReceivedExchanges;
            for (var i = 1; i < received.Count; i++)
            {
                var previous = expression(received[i - 1]);
                var current = expression(received[i]);
                var comparison = ObjectHelper.TypeCoerceCompare(received[i], previous, current);
                if (ascending ? comparison > 0 : comparison < 0)
                {
                    return $"Messages not {(ascending ? "ascending" : "descending")} by {description ?? "expression"}: message {i - 1} has " +
                        $"<{previous}> and message {i} has <{current}>";
                }
            }

            return null;
        });

    private static bool BodyEquals(Exchange exchange, object? expected)
    {
        var actual = exchange.Message.Body;
        return ValueEquals(exchange, actual, expected);
    }

    private static bool ValueEquals(Exchange exchange, object? actual, object? expected)
    {
        if (Equals(actual, expected))
        {
            return true;
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        return exchange.Context.TypeConverter.TryConvert(expected.GetType(), exchange, actual, out var converted) &&
               Equals(converted, expected);
    }

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class MockProducer(MockEndpoint endpoint) : ProducerBase(endpoint)
    {
        public override ValueTask ProcessAsync(Exchange exchange) => endpoint.OnExchangeAsync(exchange);
    }
}
