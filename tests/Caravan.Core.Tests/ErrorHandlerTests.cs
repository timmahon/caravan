using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

/// <summary>The default error handler, redelivery and error handler configuration (mirrors Camel's RedeliveryErrorHandler tests).</summary>
public class ErrorHandlerTests : ContextTestBase
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly TestTimeProvider _time = new();

    public ErrorHandlerTests()
    {
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        Context.TimeProvider = _time;
    }

    [Fact]
    public async Task Default_error_handler_logs_the_failure_and_propagates_it()
    {
        await StartWithRoutes(b => b.From("direct:start").Process(_ => throw new InvalidOperationException("Forced")).To("mock:after"));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "Hello"));

        Assert.IsType<InvalidOperationException>(e.InnerException);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        var log = Assert.Single(_logs.Messages, m => m.Category == "Caravan.Processors.DefaultErrorHandler");
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Contains("Exhausted after delivery attempt: 1", log.Message);
        Assert.Contains("System.InvalidOperationException: Forced", log.Message);
        Assert.Contains("Body: Hello", log.Message);
    }

    [Fact]
    public async Task Default_error_handler_does_not_redeliver()
    {
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start").Process(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        }));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(1, attempts);
        Assert.IsType<InvalidOperationException>(exchange.Exception);
        Assert.True(exchange.IsRedeliveryExhausted);
        Assert.False(exchange.Message.HasHeader(CaravanHeaders.Redelivered));
        Assert.IsType<InvalidOperationException>(exchange.GetProperty(ExchangePropertyKeys.ExceptionCaught));
    }

    [Fact]
    public async Task Redelivery_retries_only_the_failing_node_and_sets_headers()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3).RedeliveryDelay(0));
            b.From("direct:start")
                .To("mock:before")
                .Process(_ =>
                {
                    if (++attempts < 3)
                    {
                        throw new InvalidOperationException("attempt " + attempts);
                    }
                })
                .To("mock:after");
        });
        Mock("before").ExpectedMessageCount(1);
        Mock("after").ExpectedMessageCount(1);
        Mock("after").ExpectedHeaderReceived(CaravanHeaders.Redelivered, true);
        Mock("after").ExpectedHeaderReceived(CaravanHeaders.RedeliveryCounter, 2);
        Mock("after").ExpectedHeaderReceived(CaravanHeaders.RedeliveryMaxCounter, 3);

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(3, attempts);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Exhausted_redelivery_propagates_the_last_exception_with_the_counters()
    {
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0))
            .Process(_ => throw new InvalidOperationException("attempt " + ++attempts)));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(3, attempts);
        Assert.Equal("attempt 3", exchange.Exception!.Message);
        Assert.True(exchange.Message.GetHeader<bool>(CaravanHeaders.Redelivered));
        Assert.Equal(2, exchange.Message.GetHeader<int>(CaravanHeaders.RedeliveryCounter));
        Assert.Equal(2, exchange.Message.GetHeader<int>(CaravanHeaders.RedeliveryMaxCounter));
        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Exhausted after delivery attempt: 3"));
        Assert.Equal(2, _logs.Messages.Count(m => m.Level == LogLevel.Debug && m.Message.Contains("On delivery attempt:")));
    }

    [Fact]
    public async Task Redelivery_restores_the_message_as_it_was_before_the_first_attempt()
    {
        var bodies = new List<object?>();
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0))
            .Process(e =>
            {
                bodies.Add(e.Message.Body);
                e.Message.Body = "changed";
                e.Message.SetHeader("dirty", true);
                if (bodies.Count < 3)
                {
                    throw new InvalidOperationException();
                }
            })
            .To("mock:result"));

        await Template.SendBodyAsync("direct:start", "original");

        Assert.Equal(["original", "original", "original"], bodies);
        Assert.Equal("changed", Mock("result").ReceivedBodies.Single());
    }

    [Fact]
    public async Task Redelivery_forever_until_it_succeeds()
    {
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(-1).RedeliveryDelay(0))
            .Process(_ =>
            {
                if (++attempts < 20)
                {
                    throw new InvalidOperationException();
                }
            }));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(20, attempts);
    }

    [Fact]
    public async Task Exponential_back_off_waits_on_the_context_clock()
    {
        var times = new List<DateTimeOffset>();
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3).RedeliveryDelay(TimeSpan.FromSeconds(1)).UseExponentialBackOff())
            .Process(e =>
            {
                times.Add(_time.GetUtcNow());
                throw new InvalidOperationException();
            }));
        var timers = _time.TimersCreated;

        var send = Template.SendAsync("direct:start", e => e.Message.Body = "x");
        await _time.WaitForTimersAsync(timers + 1);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(2));
        _time.Advance(TimeSpan.FromSeconds(4));
        var exchange = await send.Bounded();

        Assert.True(exchange.IsFailed);
        Assert.Equal(4, times.Count);
        Assert.Equal([1000, 2000, 4000], times.Zip(times.Skip(1), (a, c) => (c - a).TotalMilliseconds));
    }

    [Fact]
    public async Task Redelivery_does_not_happen_before_the_delay()
    {
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(1).RedeliveryDelay(TimeSpan.FromSeconds(5)))
            .Process(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException();
                }
            })
            .To("mock:result"));
        var timers = _time.TimersCreated;

        var send = Template.SendBodyAsync("direct:start", "x");
        await _time.WaitForTimersAsync(timers + 1);
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(send.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref attempts));
        _time.Advance(TimeSpan.FromSeconds(1));
        await send.Bounded();

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Delay_pattern_sets_the_delay_per_attempt()
    {
        var times = new List<DateTimeOffset>();
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3).DelayPattern("0:100;2:1000"))
            .Process(e =>
            {
                times.Add(_time.GetUtcNow());
                throw new InvalidOperationException();
            }));
        var timers = _time.TimersCreated;

        var send = Template.SendAsync("direct:start", e => e.Message.Body = "x");
        await _time.WaitForTimersAsync(timers + 1);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(100));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(1000));
        _time.Advance(TimeSpan.FromMilliseconds(1000));
        await send.Bounded();

        Assert.Equal([100, 1000, 1000], times.Zip(times.Skip(1), (a, c) => (c - a).TotalMilliseconds));
    }

    [Fact]
    public async Task Retry_while_decides_instead_of_the_maximum()
    {
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler()
                .RedeliveryDelay(0)
                .RetryWhile(e => e.Message.GetHeader(CaravanHeaders.RedeliveryCounter, 0) < 4))
            .Process(_ => throw new InvalidOperationException("attempt " + ++attempts)));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(5, attempts);
        Assert.Equal("attempt 5", exchange.Exception!.Message);
    }

    [Fact]
    public async Task On_redelivery_runs_before_each_redelivery_and_can_change_the_message()
    {
        var seen = new List<string?>();
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler()
                .MaximumRedeliveries(2)
                .RedeliveryDelay(0)
                .OnRedelivery(e => e.Message.SetHeader("fixed", e.Message.GetHeader<int>(CaravanHeaders.RedeliveryCounter) == 2)))
            .Process(e =>
            {
                seen.Add(e.Message.GetHeader<string>("fixed"));
                if (!e.Message.GetHeader("fixed", false))
                {
                    throw new InvalidOperationException();
                }
            }));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal([null, "False", "True"], seen);
    }

    [Fact]
    public async Task On_exception_occurred_runs_for_every_failure_and_cannot_break_handling()
    {
        var occurred = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler()
                .MaximumRedeliveries(1)
                .RedeliveryDelay(0)
                .OnExceptionOccurred(e =>
                {
                    occurred.Add(e.Exception!.Message);
                    throw new InvalidOperationException("ignored");
                }))
            .ThrowException(e => new IOException("boom")));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(["boom", "boom"], occurred);
        Assert.Equal("boom", exchange.Exception!.Message);
    }

    [Fact]
    public async Task Redelivery_events_are_raised()
    {
        var events = new List<CaravanEvent>();
        Context.EventNotifiers.Add(new CoreRoutingTests.RecordingNotifier(e =>
        {
            lock (events)
            {
                events.Add(e);
            }
        }));
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0))
            .ThrowException(new InvalidOperationException()));

        await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        var redeliveries = events.Where(e => e.Type == CaravanEventType.ExchangeRedelivery).ToList();
        Assert.Equal([1, 2], redeliveries.Select(e => e.Attempt));
        Assert.Contains(events, e => e.Type == CaravanEventType.ExchangeFailed);
        Assert.DoesNotContain(events, e => e.Type == CaravanEventType.ExchangeFailureHandled);
    }

    [Fact]
    public async Task Failure_in_a_nested_block_redelivers_only_the_nested_node()
    {
        var evaluations = 0;
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0))
            .Filter(_ => ++evaluations > 0, f => f
                .To("mock:inside")
                .Process(_ => throw new InvalidOperationException("attempt " + ++attempts)))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(1, evaluations);
        Assert.Equal(3, attempts);
        Assert.Equal(1, Mock("inside").ReceivedCounter);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        Assert.Equal("attempt 3", exchange.Exception!.Message);
        Assert.Single(_logs.Messages, m => m.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Retry_attempt_log_level_is_configurable_and_retry_logging_can_be_disabled()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:warn")
                .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(1).RedeliveryDelay(0).RetryAttemptedLogLevel(LogLevel.Warning).LogStackTrace(false))
                .ThrowException(new InvalidOperationException("warn"));
            b.From("direct:quiet")
                .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(1).RedeliveryDelay(0).LogRetryAttempted(false).LogExhausted(false))
                .ThrowException(new InvalidOperationException("quiet"));
        });

        await Template.SendAsync("direct:warn", e => e.Message.Body = "x");
        await Template.SendAsync("direct:quiet", e => e.Message.Body = "x");

        Assert.Single(_logs.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("On delivery attempt: 1"));
        Assert.DoesNotContain(_logs.Messages, m => m.Message.Contains("quiet"));
    }

    [Fact]
    public async Task No_error_handler_does_not_wrap_nodes_nor_apply_on_exception()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:handled"));
            b.From("direct:start")
                .ErrorHandler(b.NoErrorHandler())
                .ThrowException(new InvalidOperationException("raw"));
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("raw", exchange.Exception!.Message);
        Assert.False(exchange.IsRedeliveryExhausted);
        Assert.Equal(0, Mock("handled").ReceivedCounter);
        Assert.DoesNotContain(_logs.Messages, m => m.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Route_error_handler_overrides_the_builder_which_overrides_the_context()
    {
        Context.ErrorHandler = new DeadLetterChannelDefinition("mock:context-dead");
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:builder-dead"));
            b.From("direct:builder").ThrowException(new InvalidOperationException());
            b.From("direct:route").ErrorHandler(b.DeadLetterChannel("mock:route-dead")).ThrowException(new InvalidOperationException());
        }));
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:context").ThrowException(new InvalidOperationException())));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:builder", "b");
        await Template.SendBodyAsync("direct:route", "r");
        await Template.SendBodyAsync("direct:context", "c");

        Assert.Equal(["b"], Mock("builder-dead").ReceivedBodies);
        Assert.Equal(["r"], Mock("route-dead").ReceivedBodies);
        Assert.Equal(["c"], Mock("context-dead").ReceivedBodies);
    }

    [Fact]
    public async Task Sub_route_error_handler_handles_the_failure_and_the_caller_sees_the_result()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").ErrorHandler(b.DeadLetterChannel("mock:caller-dead")).To("direct:sub").To("mock:caller-after");
            b.From("direct:sub")
                .OnException<IOException>(o => o.Handled(true).SetBody(_ => "handled in sub"))
                .ThrowException(new IOException("sub failed"));
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "x");

        Assert.Equal("handled in sub", reply);
        Assert.Equal(0, Mock("caller-dead").ReceivedCounter);
        // As in Camel, a handled exchange stops routing, also in the calling route.
        Assert.Equal(0, Mock("caller-after").ReceivedCounter);
    }

    [Fact]
    public async Task Sub_route_default_error_handler_exhausts_the_failure_so_the_caller_does_not_handle_it_again()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .ErrorHandler(b.DeadLetterChannel("mock:caller-dead").MaximumRedeliveries(3).RedeliveryDelay(0))
                .To("direct:sub");
            b.From("direct:sub").Process(_ => throw new InvalidOperationException("attempt " + ++attempts));
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(1, attempts);
        Assert.Equal("attempt 1", exchange.Exception!.Message);
        Assert.Equal(0, Mock("caller-dead").ReceivedCounter);
    }

    [Fact]
    public async Task Sub_route_without_error_handler_lets_the_caller_redeliver_and_handle()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .ErrorHandler(b.DeadLetterChannel("mock:caller-dead").MaximumRedeliveries(2).RedeliveryDelay(0))
                .To("direct:sub");
            b.From("direct:sub").ErrorHandler(b.NoErrorHandler()).To("mock:sub").Process(_ => throw new InvalidOperationException("attempt " + ++attempts));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(3, attempts);
        Assert.Equal(3, Mock("sub").ReceivedCounter);
        var dead = Assert.Single(Mock("caller-dead").ReceivedExchanges);
        Assert.Equal("attempt 3", dead.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
    }

    [Fact]
    public async Task Cancelled_exchange_stops_redelivering()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(5).RedeliveryDelay(TimeSpan.FromSeconds(1)))
            .Process(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException();
            }));
        var timers = _time.TimersCreated;

        var send = Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "x";
            e.CancellationToken = cts.Token;
        });
        await _time.WaitForTimersAsync(timers + 1);
        await cts.CancelAsync();
        var exchange = await send.Bounded();

        Assert.Equal(1, attempts);
        Assert.IsType<InvalidOperationException>(exchange.Exception);
    }
}
