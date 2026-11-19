using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Model;
using Caravan.Processors;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Caravan.Core.Tests;

/// <summary>circuitBreaker semantics (mirrors Camel's Resilience4j route tests: ResilienceRouteOkTest, ResilienceRouteFallbackTest, ResilienceTimeoutTest, ...).</summary>
public class CircuitBreakerTests : ContextTestBase
{
    private TestTimeProvider UseFakeTime()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        return time;
    }

    private CircuitBreakerProcessor Breaker(string routeId = "r") =>
        ((Route)Context.GetRoute(routeId)!).RouteContext.Services.OfType<CircuitBreakerProcessor>().Single();

    private Task<Exchange> Send(object? body, bool fail = false) =>
        Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = body;
            e.Message.SetHeader("fail", fail);
        });

    /// <summary>The circuit state seen by the last call of the flaky route's block.</summary>
    private CircuitBreakerState? StateInside { get; set; }

    private static bool? Property(Exchange exchange, string name) => exchange.GetProperty<bool?>(name);

    private static void AssertResponse(Exchange exchange, bool successful, bool fromFallback, bool shortCircuited = false, bool timedOut = false, bool rejected = false)
    {
        Assert.Equal(successful, Property(exchange, ExchangePropertyKeys.ResponseSuccessfulExecution));
        Assert.Equal(fromFallback, Property(exchange, ExchangePropertyKeys.ResponseFromFallback));
        Assert.Equal(shortCircuited, Property(exchange, ExchangePropertyKeys.ResponseShortCircuited));
        Assert.Equal(timedOut, Property(exchange, ExchangePropertyKeys.ResponseTimedOut));
        Assert.Equal(rejected, Property(exchange, ExchangePropertyKeys.ResponseRejected));
    }

    /// <summary>A route whose breaker block fails when the <c>fail</c> header is true; counts block calls.</summary>
    private async Task<Func<int>> StartFlakyRoute(Action<CircuitBreakerBuilder> configure, bool withFallback = true)
    {
        var calls = 0;
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb
                    .Process(e =>
                    {
                        Interlocked.Increment(ref calls);
                        StateInside = Breaker().State;
                        if (e.Message.GetHeader<bool>("fail"))
                        {
                            throw new IOException("remote down");
                        }
                    })
                    .Transform(e => "ok:" + e.Message.Body),
                configure,
                fallback: withFallback ? f => f.Transform(_ => "fallback") : null)
            .Id("cb")
            .To("mock:result"));
        return () => Volatile.Read(ref calls);
    }

    private static void Thresholds(CircuitBreakerBuilder c) =>
        c.FailureRatio(0.5).MinimumThroughput(4).SamplingDuration(TimeSpan.FromSeconds(10)).BreakDuration(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Successful_call_returns_the_block_result_and_sets_the_response_properties()
    {
        await StartFlakyRoute(_ => { });
        Mock("result").ExpectedBodiesReceived("ok:a");

        var exchange = await Send("a");

        Assert.Null(exchange.Exception);
        Assert.Equal("ok:a", exchange.Message.Body);
        AssertResponse(exchange, successful: true, fromFallback: false);
        Assert.Equal("Closed", exchange.GetProperty<string>(ExchangePropertyKeys.CircuitBreakerState));
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Failure_runs_the_fallback_on_the_message_as_it_was_before_the_breaker()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .CircuitBreaker(
                cb => cb.SetHeader("inside", _ => true).Transform(_ => "changed").ThrowException(new IOException("boom")),
                fallback: f => f.Process(e => e.Message.Body = $"fallback:{e.Message.Body}:{e.Message.GetHeader("inside") ?? "none"}"))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("fallback:a:none");

        var exchange = await Send("a");

        Assert.Null(exchange.Exception);
        AssertResponse(exchange, successful: false, fromFallback: true);
        Assert.IsType<IOException>(exchange.GetProperty(ExchangePropertyKeys.ExceptionCaught));
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Without_fallback_the_exception_propagates()
    {
        await StartFlakyRoute(_ => { }, withFallback: false);
        ExpectNone("result");

        var exchange = await Send("a", fail: true);

        Assert.IsType<IOException>(exchange.Exception);
        AssertResponse(exchange, successful: false, fromFallback: false);
        var thrown = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:start", "b", "fail", true));
        Assert.IsType<IOException>(thrown.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Fallback_failure_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .CircuitBreaker(cb => cb.ThrowException(new IOException("primary")), fallback: f => f.ThrowException(new InvalidOperationException("fallback"))));

        var exchange = await Send("a");

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        AssertResponse(exchange, successful: false, fromFallback: true);
    }

    [Fact]
    public async Task Circuit_opens_once_failure_ratio_and_minimum_throughput_are_reached()
    {
        UseFakeTime();
        var calls = await StartFlakyRoute(Thresholds);
        var registry = Context.GetCircuitBreakerStateRegistry();

        await Send(1);
        await Send(2);
        await Send(3, fail: true);
        Assert.Equal(CircuitBreakerState.Closed, registry.GetState("cb"));
        var fourth = await Send(4, fail: true);
        AssertResponse(fourth, successful: false, fromFallback: true);
        Assert.Equal(CircuitBreakerState.Open, registry.GetState("cb"));
        Assert.Equal(CircuitBreakerState.Open, Breaker().State);

        var shortCircuited = await Send(5);

        Assert.Equal(4, calls());
        Assert.Equal("fallback", shortCircuited.Message.Body);
        AssertResponse(shortCircuited, successful: false, fromFallback: true, shortCircuited: true, rejected: true);
        Assert.IsType<BrokenCircuitException>(shortCircuited.GetProperty(ExchangePropertyKeys.ExceptionCaught));
        Assert.Equal("Open", shortCircuited.GetProperty<string>(ExchangePropertyKeys.CircuitBreakerState));
    }

    [Fact]
    public async Task Circuit_does_not_open_below_the_minimum_throughput()
    {
        UseFakeTime();
        var calls = await StartFlakyRoute(Thresholds);

        for (var i = 0; i < 3; i++)
        {
            await Send(i, fail: true);
        }

        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
        await Send(3);
        Assert.Equal(4, calls());
        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
    }

    [Fact]
    public async Task Failures_outside_the_sampling_duration_are_forgotten()
    {
        var time = UseFakeTime();
        await StartFlakyRoute(Thresholds);

        await Send(1, fail: true);
        await Send(2, fail: true);
        await Send(3, fail: true);
        time.Advance(TimeSpan.FromSeconds(11));
        await Send(4, fail: true);

        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
    }

    [Fact]
    public async Task Half_open_probe_success_closes_the_circuit()
    {
        var time = UseFakeTime();
        var calls = await StartFlakyRoute(Thresholds);
        for (var i = 0; i < 4; i++)
        {
            await Send(i, fail: true);
        }

        Assert.Equal(CircuitBreakerState.Open, Breaker().State);
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(Property(await Send("still open"), ExchangePropertyKeys.ResponseShortCircuited));
        time.Advance(TimeSpan.FromSeconds(1));

        // Polly moves to half-open when the next call arrives: that call is the probe.
        var probe = await Send("probe");

        Assert.Equal(CircuitBreakerState.HalfOpen, StateInside);
        Assert.Equal("ok:probe", probe.Message.Body);
        AssertResponse(probe, successful: true, fromFallback: false);
        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
        Assert.Equal(5, calls());
        Assert.Equal("ok:next", (await Send("next")).Message.Body);
    }

    [Fact]
    public async Task Half_open_probe_failure_opens_the_circuit_again()
    {
        var time = UseFakeTime();
        var calls = await StartFlakyRoute(Thresholds);
        for (var i = 0; i < 4; i++)
        {
            await Send(i, fail: true);
        }

        time.Advance(TimeSpan.FromSeconds(5));
        var probe = await Send("probe", fail: true);

        AssertResponse(probe, successful: false, fromFallback: true);
        Assert.Equal(CircuitBreakerState.Open, Breaker().State);
        Assert.True(Property(await Send("again"), ExchangePropertyKeys.ResponseShortCircuited));
        Assert.Equal(5, calls());
    }

    [Fact]
    public async Task Short_circuit_without_fallback_fails_with_broken_circuit_exception()
    {
        UseFakeTime();
        var calls = await StartFlakyRoute(Thresholds, withFallback: false);
        for (var i = 0; i < 4; i++)
        {
            await Send(i, fail: true);
        }

        var exchange = await Send("x");

        Assert.IsType<BrokenCircuitException>(exchange.Exception);
        AssertResponse(exchange, successful: false, fromFallback: false, shortCircuited: true, rejected: true);
        Assert.Equal(4, calls());
    }

    [Fact]
    public async Task Timeout_abandons_the_call_and_runs_the_fallback()
    {
        var time = UseFakeTime();
        var started = TestHelpers.NewSignal();
        var release = TestHelpers.NewSignal();
        var finished = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb.Process(async e =>
                {
                    started.TrySetResult();
                    await release.Task;
                    e.Message.Body = "late";
                    finished.TrySetResult();
                }),
                c => c.Timeout(TimeSpan.FromSeconds(1)),
                fallback: f => f.Transform(_ => "fallback"))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("fallback");

        var send = Send("a");
        await started.Task.Bounded();
        time.Advance(TimeSpan.FromSeconds(1));
        var exchange = await send.Bounded();

        Assert.Equal("fallback", exchange.Message.Body);
        AssertResponse(exchange, successful: false, fromFallback: true, timedOut: true);
        Assert.IsType<TimeoutRejectedException>(exchange.GetProperty(ExchangePropertyKeys.ExceptionCaught));
        await Mock("result").AssertIsSatisfiedAsync();

        // The abandoned call finishes on its own copy and does not touch the exchange.
        release.TrySetResult();
        await finished.Task.Bounded();
        Assert.Equal("fallback", exchange.Message.Body);
    }

    [Fact]
    public async Task Timeout_cancels_the_token_of_a_cooperative_call()
    {
        var time = UseFakeTime();
        var started = TestHelpers.NewSignal();
        var cancelled = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start")
            .CircuitBreaker(
                cb => cb.Process(async e =>
                {
                    started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, e.CancellationToken);
                    }
                    finally
                    {
                        cancelled.TrySetResult();
                    }
                }),
                c => c.Timeout(TimeSpan.FromMilliseconds(500))));

        var send = Send("a");
        await started.Task.Bounded();
        time.Advance(TimeSpan.FromMilliseconds(500));
        var exchange = await send.Bounded();

        await cancelled.Task.Bounded();
        Assert.IsType<TimeoutRejectedException>(exchange.Exception);
        AssertResponse(exchange, successful: false, fromFallback: false, timedOut: true);
    }

    [Fact]
    public async Task Timeouts_count_as_failures_of_the_circuit()
    {
        var time = UseFakeTime();
        var entered = new SemaphoreSlim(0);
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb.Process(e =>
                {
                    entered.Release();
                    return Task.Delay(Timeout.InfiniteTimeSpan, e.CancellationToken);
                }),
                c => c.FailureRatio(1).MinimumThroughput(2).Timeout(TimeSpan.FromSeconds(1)),
                fallback: f => f.Transform(_ => "fallback")));

        for (var i = 0; i < 2; i++)
        {
            var send = Send(i);
            Assert.True(await entered.WaitAsync(TestHelpers.Wait));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(Property(await send.Bounded(), ExchangePropertyKeys.ResponseTimedOut));
        }

        Assert.Equal(CircuitBreakerState.Open, Breaker().State);
    }

    [Fact]
    public async Task Bulkhead_rejects_calls_beyond_the_concurrency_limit()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(cb => cb.Process(_ => probe.EnterAsync()), c => c.Bulkhead(1), fallback: f => f.Transform(_ => "rejected"))
            .To("mock:result"));

        var first = Task.Run(() => Send("first"));
        await probe.WaitForStartedAsync(1);
        var second = await Send("second").Bounded();
        probe.Release();
        var firstResult = await first.Bounded();

        Assert.Equal("rejected", second.Message.Body);
        AssertResponse(second, successful: false, fromFallback: true, rejected: true);
        Assert.IsType<BulkheadRejectedException>(second.GetProperty(ExchangePropertyKeys.ExceptionCaught));
        AssertResponse(firstResult, successful: true, fromFallback: false);
        Assert.Equal(1, probe.Max);
        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
    }

    [Fact]
    public async Task Bulkhead_queues_waiting_calls_and_rejects_beyond_the_queue()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(cb => cb.Process(_ => probe.EnterAsync()), c => c.Bulkhead(1, 1)));

        var first = Task.Run(() => Send("first"));
        await probe.WaitForStartedAsync(1);
        var second = Task.Run(() => Send("second"));
        await TestHelpers.WaitUntilAsync(() => Breaker().BulkheadWaiting == 1);
        var third = await Send("third").Bounded();

        Assert.IsType<BulkheadRejectedException>(third.Exception);
        AssertResponse(third, successful: false, fromFallback: false, rejected: true);
        probe.Release();
        await probe.WaitForStartedAsync(2);
        probe.Release();
        Assert.Null((await first.Bounded()).Exception);
        Assert.Null((await second.Bounded()).Exception);
        Assert.Equal(1, probe.Max);
        Assert.Equal(0, Breaker().BulkheadRunning);
    }

    [Fact]
    public async Task Route_error_handler_does_not_redeliver_inside_the_breaker()
    {
        var calls = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(3));
            b.From("direct:start")
                .CircuitBreaker(cb => cb.Process(_ =>
                {
                    Interlocked.Increment(ref calls);
                    throw new IOException("down");
                }))
                .To("mock:result");
        });
        Mock("dead").ExpectedMessageCount(1);
        ExpectNone("result");

        var exchange = await Send("a");

        Assert.Equal(1, calls);
        Assert.Null(exchange.Exception);
        Assert.True(exchange.ErrorHandlerHandled);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task On_exception_handles_a_failure_leaving_the_breaker()
    {
        var calls = 0;
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).Transform(_ => "handled")).MaximumRedeliveries(2);
            b.From("direct:start")
                .CircuitBreaker(cb => cb.Process(_ =>
                {
                    Interlocked.Increment(ref calls);
                    throw new IOException("down");
                }));
        });

        var result = await Template.RequestBodyAsync("direct:start", "a");

        Assert.Equal("handled", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Inherit_error_handler_redelivers_inside_the_breaker()
    {
        var calls = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3));
            b.From("direct:start")
                .CircuitBreaker(
                    cb => cb.Process(_ =>
                    {
                        if (Interlocked.Increment(ref calls) < 3)
                        {
                            throw new IOException("flaky");
                        }
                    }).Transform(_ => "done"),
                    c => c.InheritErrorHandler());
        });

        var exchange = await Send("a");

        Assert.Null(exchange.Exception);
        Assert.Equal("done", exchange.Message.Body);
        Assert.Equal(3, calls);
        AssertResponse(exchange, successful: true, fromFallback: false);
    }

    [Fact]
    public async Task Block_changes_and_called_routes_are_kept_on_success()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .CircuitBreaker(cb => cb.To("direct:remote").SetHeader("h", _ => "v").SetProperty("p", _ => 1))
                .To("mock:result");
            b.From("direct:remote").Transform(e => "remote:" + e.Message.Body);
        });
        Mock("result").ExpectedBodiesReceived("remote:a");
        Mock("result").ExpectedHeaderReceived("h", "v");

        var exchange = await Send("a");

        Assert.Equal(1, exchange.GetProperty<int>("p"));
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Completion_callbacks_registered_inside_the_breaker_run_with_the_exchange()
    {
        var sync = new TestHelpers.RecordingSynchronization();
        await StartWithRoutes(b => b.From("direct:start")
            .CircuitBreaker(cb => cb.Process(e => e.AddOnCompletion(sync)))
            .Process(_ => Assert.Null(sync.Succeeded)));

        await Send("a");

        Assert.True(await sync.Completed.Task.Bounded() is not null);
        Assert.True(sync.Succeeded);
    }

    [Fact]
    public async Task Ignored_exceptions_do_not_open_the_circuit_but_still_use_the_fallback()
    {
        UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb.ThrowException(new ArgumentException("bad input")),
                c => c.FailureRatio(0.5).MinimumThroughput(2).IgnoreExceptions(typeof(ArgumentException)),
                fallback: f => f.Transform(_ => "fallback")));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("fallback", (await Send(i)).Message.Body);
        }

        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
    }

    [Fact]
    public async Task Record_exceptions_limits_what_counts_as_failure()
    {
        UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb.Process(e => throw (e.Message.GetHeader<bool>("fail") ? new IOException("io") : new ArgumentException("arg"))),
                c => c.FailureRatio(0.5).MinimumThroughput(2).RecordExceptions(typeof(IOException)),
                fallback: f => f.Transform(_ => "fallback")));

        await Send(1);
        await Send(2);
        await Send(3);
        Assert.Equal(CircuitBreakerState.Closed, Breaker().State);
        await Send(4, fail: true);
        await Send(5, fail: true);
        await Send(6, fail: true);
        Assert.Equal(CircuitBreakerState.Open, Breaker().State);
    }

    [Fact]
    public async Task Named_configuration_is_shared_and_inline_options_override_it()
    {
        Context.AddCircuitBreakerConfiguration("shared", new CircuitBreakerConfiguration
        {
            FailureRatio = 1,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(7),
            Timeout = TimeSpan.FromSeconds(3),
        });
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").CircuitBreaker(cb => cb.To("mock:a"), c => c.ConfigurationRef("shared"));
            b.From("direct:b").RouteId("b").CircuitBreaker(cb => cb.To("mock:b"), c => c.ConfigurationRef("shared").MinimumThroughput(5).Bulkhead(2));
        });

        var a = Breaker("a").Configuration;
        var b = Breaker("b").Configuration;

        Assert.Equal(1, a.FailureRatio);
        Assert.Equal(2, a.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(7), a.BreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), a.Timeout);
        Assert.Null(a.BulkheadMaxConcurrentCalls);
        Assert.Equal(1, b.FailureRatio);
        Assert.Equal(5, b.MinimumThroughput);
        Assert.Equal(2, b.BulkheadMaxConcurrentCalls);
    }

    [Fact]
    public async Task Configuration_can_come_from_the_registry_and_the_context_default()
    {
        Context.SetDefaultCircuitBreakerConfiguration(new CircuitBreakerConfiguration { SamplingDuration = TimeSpan.FromSeconds(20), MinimumThroughput = 50 });
        Context.Registry.Bind("fromRegistry", new CircuitBreakerConfiguration { MinimumThroughput = 3 });
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").CircuitBreaker(cb => cb.To("mock:a"), c => c.ConfigurationRef("fromRegistry"));
            b.From("direct:b").RouteId("b").CircuitBreaker(cb => cb.To("mock:b"));
        });

        Assert.Equal(3, Breaker("a").Configuration.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(20), Breaker("a").Configuration.SamplingDuration);
        Assert.Equal(50, Breaker("b").Configuration.MinimumThroughput);
    }

    [Fact]
    public async Task Missing_named_configuration_fails_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:a").CircuitBreaker(cb => cb.To("mock:a"), c => c.ConfigurationRef("missing"))));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());

        Assert.IsType<NoSuchBeanException>(e.InnerException);
    }

    [Fact]
    public async Task Shared_named_configuration_still_gives_each_breaker_its_own_circuit()
    {
        UseFakeTime();
        Context.AddCircuitBreakerConfiguration("shared", new CircuitBreakerConfiguration { FailureRatio = 1, MinimumThroughput = 2 });
        await StartWithRoutes(b =>
        {
            b.From("direct:a").CircuitBreaker(cb => cb.ThrowException(new IOException()), c => c.ConfigurationRef("shared"), f => f.Stop()).Id("cbA");
            b.From("direct:b").CircuitBreaker(cb => cb.To("mock:b"), c => c.ConfigurationRef("shared")).Id("cbB");
        });

        await Template.SendBodyAsync("direct:a", 1);
        await Template.SendBodyAsync("direct:a", 2);

        var registry = Context.GetCircuitBreakerStateRegistry();
        Assert.Equal(CircuitBreakerState.Open, registry.GetState("cbA"));
        Assert.Equal(CircuitBreakerState.Closed, registry.GetState("cbB"));
        Assert.Contains("cbA", registry.Ids);
        Assert.Null(registry.GetState("unknown"));
    }

    [Fact]
    public async Task Circuit_can_be_isolated_and_closed_through_the_state_registry()
    {
        var calls = await StartFlakyRoute(_ => { });
        var registry = Context.GetCircuitBreakerStateRegistry();

        await registry.IsolateAsync("cb");
        var isolated = await Send("a");

        Assert.Equal(CircuitBreakerState.Isolated, registry.GetState("cb"));
        AssertResponse(isolated, successful: false, fromFallback: true, shortCircuited: true, rejected: true);
        Assert.Equal(0, calls());

        await registry.CloseAsync("cb");
        Assert.Equal("ok:b", (await Send("b")).Message.Body);
        Assert.Equal(CircuitBreakerState.Closed, registry.GetState("cb"));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.CloseAsync("unknown"));
    }

    [Fact]
    public async Task Circuit_state_survives_a_route_restart()
    {
        UseFakeTime();
        await StartFlakyRoute(c => c.FailureRatio(1).MinimumThroughput(2));
        await Send(1, fail: true);
        await Send(2, fail: true);

        await Context.StopRouteAsync("r");
        await Context.StartRouteAsync("r");

        Assert.Equal(CircuitBreakerState.Open, Context.GetCircuitBreakerStateRegistry().GetState("cb"));
        Assert.True(Property(await Send(3), ExchangePropertyKeys.ResponseShortCircuited));
    }

    [Fact]
    public async Task Fallback_via_network_has_its_own_circuit()
    {
        UseFakeTime();
        var fallbackCalls = 0;
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .CircuitBreaker(
                cb => cb.ThrowException(new IOException("primary")),
                c => c.FailureRatio(1).MinimumThroughput(2).OnFallbackViaNetwork(f => f.Process(_ =>
                {
                    Interlocked.Increment(ref fallbackCalls);
                    throw new IOException("fallback service down");
                })))
            .Id("cb"));
        var registry = Context.GetCircuitBreakerStateRegistry();

        Assert.IsType<IOException>((await Send(1)).Exception);
        Assert.IsType<IOException>((await Send(2)).Exception);
        var third = await Send(3);

        Assert.IsType<BrokenCircuitException>(third.Exception);
        Assert.Equal(2, fallbackCalls);
        Assert.Equal(CircuitBreakerState.Open, registry.GetState("cb-fallback"));
        Assert.Equal(CircuitBreakerState.Open, Breaker().FallbackState);
    }

    [Fact]
    public async Task Fallback_via_network_result_is_used_when_it_succeeds()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .CircuitBreaker(cb => cb.ThrowException(new IOException()), c => c.OnFallbackViaNetwork(f => f.To("direct:backup")))
            .To("mock:result"));
        await Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("direct:backup").Transform(e => "backup:" + e.Message.Body)));
        Mock("result").ExpectedBodiesReceived("backup:a");

        var exchange = await Send("a");

        AssertResponse(exchange, successful: false, fromFallback: true);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public void Only_one_fallback_is_allowed()
    {
        var e = Assert.Throws<InvalidOperationException>(() => RouteBuilder.Create(b => b.From("direct:a")
            .CircuitBreaker(cb => cb.To("mock:a"), c => c.OnFallback(_ => { }), fallback: _ => { })).Build(Context));

        Assert.Contains("one fallback", e.Message);
    }

    [Fact]
    public void Definition_exposes_block_and_fallback_as_children()
    {
        var route = RouteBuilder.Create(b => b.From("direct:a")
            .CircuitBreaker(cb => cb.To("mock:a"), fallback: f => f.To("mock:fallback"))).Build(Context).Single();

        var labels = route.AllNodes().Select(n => n.ShortName).ToList();

        Assert.Equal(["circuitBreaker", "to", "onFallback", "to"], labels);
    }
}
