using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Mock;

/// <summary>
/// The <c>mock</c> component for testing: records received exchanges and verifies expectations.
/// <para>URI: <c>mock:name[?retainFirst=..&amp;retainLast=..]</c></para>
/// </summary>
public sealed class MockComponent : ComponentBase
{
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        new MockEndpoint(uri, this, remaining);
}

/// <summary>Raised when a mock endpoint's expectations are not met.</summary>
public sealed class MockAssertionException(string message) : Exception(message);

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
    private TaskCompletionSource _countReached = NewTcs();
    private int _receivedCounter;
    private int _expectedCount = -1;
    private int _expectedMinimumCount = -1;
    private IProcessor? _defaultProcessor;
    private Exception? _failure;

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

    /// <summary>Simulated processing delay per message.</summary>
    public TimeSpan? Delay { get; set; }

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

    public IReadOnlyList<object?> ReceivedBodies => ReceivedExchanges.Select(e => e.Message.Body).ToList();

    public IReadOnlyList<T?> GetReceivedBodies<T>() => ReceivedExchanges.Select(e => e.Message.GetBody<T>()).ToList();

    public override bool IsLenientProperties => false;

    public override IProducer CreateProducer() => new MockProducer(this);

    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The mock component does not support consumers");

    // ----- Expectations -----

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

    /// <summary>Sets the reply body for every exchange.</summary>
    public MockEndpoint ReturnReplyBody(Func<Exchange, object?> body) =>
        WhenAnyExchangeReceived(e => e.Message.Body = body(e));

    // ----- Assertions -----

    public async Task AssertIsSatisfiedAsync(TimeSpan? timeout = null)
    {
        var error = await WaitAndCheckAsync(timeout).ConfigureAwait(false);
        if (error is not null)
        {
            throw new MockAssertionException($"{EndpointUri}: {error}");
        }
    }

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

    public void Reset()
    {
        lock (_lock)
        {
            _received.Clear();
            _expectations.Clear();
            _processorsByIndex.Clear();
            _defaultProcessor = null;
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
            try
            {
                await _countReached.Task.WaitAsync(wait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // fall through to the checks
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
        var count = ReceivedCounter;
        if ((_expectedCount > 0 && count >= _expectedCount) || (_expectedMinimumCount > 0 && count >= _expectedMinimumCount))
        {
            _countReached.TrySetResult();
        }
    }

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

public static class MockContextExtensions
{
    /// <summary>Gets (creating if needed) a mock endpoint, e.g. <c>context.GetMockEndpoint("mock:result")</c>.</summary>
    public static MockEndpoint GetMockEndpoint(this ICaravanContext context, string uri) =>
        context.GetEndpoint<MockEndpoint>(uri.StartsWith("mock:", StringComparison.Ordinal) ? uri : "mock:" + uri);
}
