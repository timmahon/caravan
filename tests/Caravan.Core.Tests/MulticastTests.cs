using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>multicast semantics (mirrors Camel's Multicast* tests).</summary>
public class MulticastTests : ContextTestBase
{
    [Fact]
    public async Task Each_branch_gets_a_correlated_copy_and_the_latest_reply_is_the_result()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .Multicast(m => m.To("direct:x").To("direct:y").To("direct:z"))
                .To("mock:result");
            b.From("direct:x").Process(e => e.Message.Body = e.Message.Body + "x").To("mock:x");
            b.From("direct:y").Process(e => e.Message.Body = e.Message.Body + "y").To("mock:y");
            b.From("direct:z").Process(e => e.Message.Body = e.Message.Body + "z").To("mock:z");
        });
        Mock("x").ExpectedBodiesReceived("Ax");
        Mock("y").ExpectedBodiesReceived("Ay");
        Mock("z").ExpectedBodiesReceived("Az");
        Mock("result").ExpectedBodiesReceived("Az");

        var sent = await Template.SendAsync("direct:start", e => e.Message.Body = "A");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var x = Mock("x").ReceivedExchanges[0];
        Assert.NotEqual(sent.ExchangeId, x.ExchangeId);
        Assert.Equal(sent.ExchangeId, x.GetProperty(ExchangePropertyKeys.CorrelationId));
        Assert.Equal(0, x.GetProperty(ExchangePropertyKeys.MulticastIndex));
        Assert.Equal(false, x.GetProperty(ExchangePropertyKeys.MulticastComplete));
        Assert.Equal(2, Mock("z").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.MulticastIndex));
        Assert.Equal(true, Mock("z").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.MulticastComplete));
        Assert.Equal("Az", sent.Message.Body);
        Assert.Equal(sent.ExchangeId, sent.ExchangeId);
        Assert.Null(sent.GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task Branches_run_in_order_sequentially_and_do_not_see_each_others_changes()
    {
        var order = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .SetHeader("h", Expr.Constant("original"))
            .Multicast(m => m
                .Process(e =>
                {
                    order.Add("a:" + e.Message.GetHeader("h"));
                    e.Message.SetHeader("h", "changed by a");
                })
                .Process(e => order.Add("b:" + e.Message.GetHeader("h")))));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["a:original", "b:original"], order);
    }

    [Fact]
    public async Task Custom_aggregation_strategy_combines_the_replies()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m => m.Transform(_ => "a").Transform(_ => "b").Transform(_ => "c"),
                aggregationStrategy: AggregationStrategies.From((old, @new) =>
                {
                    if (old is null)
                    {
                        return @new;
                    }

                    old.Message.Body = old.Message.GetBody<string>() + "+" + @new.Message.GetBody<string>();
                    return old;
                }))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("a+b+c");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Aggregation_strategy_can_be_looked_up_in_the_registry()
    {
        Context.Registry.Bind("myStrategy", AggregationStrategies.String("|"));
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.Transform(_ => "a").Transform(_ => "b"), aggregationStrategyRef: "myStrategy"));

        Assert.Equal("a|b", await Template.RequestBodyAsync<string>("direct:start", "x"));
    }

    [Fact]
    public async Task Unknown_aggregation_strategy_ref_fails_the_route_on_start()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").Multicast(m => m.To("mock:a"), aggregationStrategyRef: "missing")));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Context.StartAsync());

        Assert.Contains("missing", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_branch_fails_the_exchange_after_all_branches_ran()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.To("mock:a").ThrowException(new InvalidOperationException("boom")).To("mock:c"))
            .To("mock:result"));
        Mock("a").ExpectedMessageCount(1);
        Mock("c").ExpectedMessageCount(1);
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_on_exception_skips_the_remaining_branches_and_wraps_the_exception()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m => m.To("mock:a").ThrowException(new InvalidOperationException("boom")).To("mock:c"),
                stopOnException: true)
            .To("mock:result"));
        Mock("a").ExpectedMessageCount(1);
        ExpectNone("c");
        ExpectNone("result");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        var wrapped = Assert.IsType<CaravanExchangeException>(exchange.Exception);
        Assert.StartsWith("Multicast processing failed for number 1", wrapped.Message, StringComparison.Ordinal);
        Assert.Equal("boom", wrapped.InnerException!.Message);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Failure_handled_by_the_error_handler_in_a_branch_does_not_fail_the_exchange()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start")
                .Multicast(m => m.To("mock:a").Pipeline(p => p.SetBody(_ => "bad").ThrowException(new InvalidOperationException("boom"))).To("mock:c"))
                .To("mock:result");
        });
        Mock("dead").ExpectedBodiesReceived("bad");
        Mock("c").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_on_exception_with_a_handled_failure_stops_and_routing_ends_as_handled()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start")
                .Multicast(m => m.To("mock:a").ThrowException(new InvalidOperationException("boom")).To("mock:c"), stopOnException: true)
                .To("mock:result");
        });
        Mock("dead").ExpectedMessageCount(1);
        ExpectNone("c");
        ExpectNone("result");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Null(exchange.Exception);
        Assert.True(exchange.ErrorHandlerHandled);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Branch_failure_is_redelivered_by_the_branch_and_not_again_by_the_route()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:start")
                .Multicast(m => m.Process(_ =>
                {
                    attempts++;
                    throw new InvalidOperationException("boom");
                }))
                .To("mock:result");
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Parallel_branches_run_concurrently_and_aggregate_in_branch_order()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m => m
                    .Process(async e =>
                    {
                        await probe.EnterAsync();
                        e.Message.Body = "a";
                    })
                    .Process(async e =>
                    {
                        await probe.EnterAsync();
                        e.Message.Body = "b";
                    })
                    .Process(async e =>
                    {
                        await probe.EnterAsync();
                        e.Message.Body = "c";
                    }),
                parallelProcessing: true,
                aggregationStrategy: AggregationStrategies.String(","))
            .To("mock:result"));

        var reply = Template.RequestBodyAsync<string>("direct:start", "x");
        await probe.WaitForStartedAsync(3);
        probe.Release(3);

        Assert.Equal("a,b,c", await reply.Bounded());
        Assert.Equal(3, probe.Max);
    }

    [Fact]
    public async Task Parallel_aggregation_is_in_branch_order_even_when_branches_complete_in_reverse()
    {
        var gates = Enumerable.Range(0, 3).Select(_ => TestHelpers.NewSignal()).ToArray();
        var completions = new List<int>();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m =>
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var index = i;
                        m.Process(async e =>
                        {
                            await gates[index].Task;
                            e.Message.Body = index;
                            lock (completions)
                            {
                                completions.Add(index);
                            }

                            if (index > 0)
                            {
                                gates[index - 1].TrySetResult();
                            }
                        });
                    }
                },
                parallelProcessing: true,
                aggregationStrategy: AggregationStrategies.GroupedBody()));

        var reply = Template.RequestBodyAsync("direct:start", "x");
        gates[2].SetResult();

        Assert.Equal(new object?[] { 0, 1, 2 }, Assert.IsType<List<object?>>(await reply.Bounded()));
        Assert.Equal([2, 1, 0], completions);
    }

    [Fact]
    public async Task Parallel_streaming_aggregates_in_completion_order()
    {
        var gates = Enumerable.Range(0, 3).Select(_ => TestHelpers.NewSignal()).ToArray();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m =>
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var index = i;
                        m.Process(async e =>
                        {
                            await gates[index].Task;
                            e.Message.Body = index;
                        });
                    }
                },
                parallelProcessing: true,
                streaming: true,
                aggregationStrategy: AggregationStrategies.From((old, @new) =>
                {
                    var list = old?.Message.Body as List<int> ?? [];
                    list.Add(@new.Message.GetBody<int>());
                    var result = old ?? @new;
                    result.Message.Body = list;

                    // Each completion releases the next gate, so completion order is 2, 0, 1.
                    if (list.Count == 1)
                    {
                        gates[0].TrySetResult();
                    }
                    else if (list.Count == 2)
                    {
                        gates[1].TrySetResult();
                    }

                    return result;
                })));

        var reply = Template.RequestBodyAsync("direct:start", "x");
        gates[2].SetResult();

        Assert.Equal([2, 0, 1], Assert.IsType<List<int>>(await reply.Bounded()));
    }

    [Fact]
    public async Task Max_degree_of_parallelism_bounds_the_concurrency()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m =>
                {
                    for (var i = 0; i < 7; i++)
                    {
                        m.Process(_ => probe.EnterAsync());
                    }
                },
                parallelProcessing: true,
                maxDegreeOfParallelism: 3));

        var send = Template.SendBodyAsync("direct:start", "x");
        await probe.WaitForStartedAsync(3);
        for (var started = 4; started <= 7; started++)
        {
            probe.Release();
            await probe.WaitForStartedAsync(started);
        }

        probe.Release(3);
        await send.Bounded();

        Assert.Equal(3, probe.Max);
        Assert.Equal(7, probe.StartedCount);
    }

    [Fact]
    public async Task Parallel_stop_on_exception_starts_no_further_branch()
    {
        var started = new List<int>();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m =>
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var index = i;
                        m.Process(_ =>
                        {
                            lock (started)
                            {
                                started.Add(index);
                            }

                            if (index == 1)
                            {
                                throw new InvalidOperationException("boom");
                            }
                        });
                    }
                },
                parallelProcessing: true,
                maxDegreeOfParallelism: 1,
                stopOnException: true)
            .To("mock:result"));
        ExpectNone("result");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal([0, 1], started);
        Assert.IsType<CaravanExchangeException>(exchange.Exception);
        Assert.IsType<InvalidOperationException>(exchange.Exception!.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Parallel_timeout_ignores_unfinished_branches_and_calls_on_timeout()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var strategy = new RecordingStrategy();
        var slowStarted = TestHelpers.NewSignal();
        var slowCancelled = TestHelpers.NewSignal();
        var lastDone = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Multicast(
                m => m
                    .Transform(_ => "a")
                    .Process(async e =>
                    {
                        slowStarted.SetResult();
                        try
                        {
                            await Task.Delay(TestHelpers.Wait, e.CancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            slowCancelled.SetResult();
                            throw;
                        }
                    })
                    .Process(e =>
                    {
                        e.Message.Body = "c";
                        lastDone.SetResult();
                    }),
                parallelProcessing: true,
                timeout: TimeSpan.FromSeconds(5),
                aggregationStrategy: strategy)
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("a,c");
        var processor = ((Route)Context.GetRoute("r")!).RouteContext.Services.OfType<MulticastProcessor>().Single();

        var timers = time.TimersCreated;
        var send = Template.SendBodyAsync("direct:start", "x");
        await time.WaitForTimersAsync(timers + 1);

        // Branch c completes before the slow branch b: it waits for b to be aggregated in order, then the timeout takes it.
        await slowStarted.Task.Bounded();
        await lastDone.Task.Bounded();
        await TestHelpers.WaitUntilAsync(() => processor.RunningCount == 1);
        time.Advance(TimeSpan.FromSeconds(5));
        await send.Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal([(1, 3, TimeSpan.FromSeconds(5))], strategy.Timeouts);
        await slowCancelled.Task.Bounded();
        await TestHelpers.WaitUntilAsync(() => processor.RunningCount == 0);
    }

    [Fact]
    public async Task Parallel_aggregate_calls_the_strategy_concurrently_when_streaming()
    {
        var probe = new ConcurrencyProbe();
        var bodies = new System.Collections.Concurrent.ConcurrentBag<object?>();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m => m.Transform(_ => "a").Transform(_ => "b"),
                parallelProcessing: true,
                streaming: true,
                parallelAggregate: true,
                aggregationStrategy: AggregationStrategies.From((_, @new) =>
                {
                    Assert.True(probe.EnterAsync().Wait(TestHelpers.Wait));
                    bodies.Add(@new.Message.Body);
                    return @new;
                })));

        var send = Template.SendBodyAsync("direct:start", "x");
        await probe.WaitForStartedAsync(2);
        probe.Release(2);
        await send.Bounded();

        Assert.Equal(2, probe.Max);
        Assert.Equal(["a", "b"], bodies.Order());
    }

    [Fact]
    public async Task Stopping_the_context_waits_for_branches_abandoned_after_a_timeout()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var release = TestHelpers.NewSignal();
        var finished = false;
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Multicast(
                m => m.Process(async _ =>
                {
                    // Ignores cancellation on purpose: stopping must still wait for it.
                    await release.Task.Bounded();
                    finished = true;
                }),
                parallelProcessing: true,
                timeout: TimeSpan.FromSeconds(1)));

        var timers = time.TimersCreated;
        var send = Template.SendBodyAsync("direct:start", "x");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await send.Bounded();
        var processor = ((Route)Context.GetRoute("r")!).RouteContext.Services.OfType<MulticastProcessor>().Single();
        Assert.Equal(1, processor.RunningCount);

        var stop = Context.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.Bounded();

        Assert.True(finished);
        Assert.Equal(0, processor.RunningCount);
    }

    [Fact]
    public async Task On_prepare_runs_on_each_branch_copy_only()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(
                m => m.To("mock:a").To("mock:b"),
                onPrepare: e => e.Message.SetHeader("prepared", e.GetProperty(ExchangePropertyKeys.MulticastIndex)))
            .To("mock:result"));
        Mock("a").ExpectedHeaderReceived("prepared", 0);
        Mock("b").ExpectedHeaderReceived("prepared", 1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Branches_get_their_own_unit_of_work_completed_before_the_parent()
    {
        var order = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .Process(e => e.AddOnCompletion(new Sync(() => order.Add("parent"))))
            .Multicast(m => m.Process(e => e.AddOnCompletion(new Sync(() => order.Add("branch")))))
            .Process(_ => order.Add("after")));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["branch", "after", "parent"], order);
    }

    [Fact]
    public async Task Share_unit_of_work_runs_branch_completions_with_the_parent()
    {
        var order = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.Process(e => e.AddOnCompletion(new Sync(() => order.Add("branch")))), shareUnitOfWork: true)
            .Process(_ => order.Add("after")));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["after", "branch"], order);
    }

    [Fact]
    public async Task Stop_in_a_branch_only_stops_that_branch()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.Pipeline(p => p.Stop().To("mock:never")).To("mock:b"))
            .To("mock:result"));
        ExpectNone("never");
        Mock("b").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Exception_in_the_aggregation_strategy_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.To("mock:a"), aggregationStrategy: AggregationStrategies.From((_, _) => throw new InvalidOperationException("aggregate")))
            .To("mock:result"));
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("aggregate", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Cancelled_exchange_starts_no_further_branch()
    {
        using var cts = new CancellationTokenSource();
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.Process(_ => cts.Cancel()).To("mock:never")));
        ExpectNone("never");

        var exchange = await Template.SendAsync("direct:start", e => e.CancellationToken = cts.Token);

        Assert.IsAssignableFrom<OperationCanceledException>(exchange.Exception);
        await Mock("never").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Multicast_inside_do_try_is_caught()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.Multicast(m => m.ThrowException(new InvalidOperationException("boom"))).To("mock:never"),
                c => c.DoCatch<InvalidOperationException>(r => r.To("mock:caught")))
            .To("mock:result"));
        Mock("caught").ExpectedMessageCount(1);
        ExpectNone("never");
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    private sealed class Sync(Action onDone) : ISynchronization
    {
        public ValueTask OnCompleteAsync(Exchange exchange)
        {
            onDone();
            return ValueTask.CompletedTask;
        }

        public ValueTask OnFailureAsync(Exchange exchange)
        {
            onDone();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStrategy : IAggregationStrategy
    {
        private readonly IAggregationStrategy _inner = AggregationStrategies.String(",");

        public List<(int Index, int Total, TimeSpan Timeout)> Timeouts { get; } = [];

        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange) => _inner.Aggregate(oldExchange, newExchange);

        public void OnCompletion(Exchange exchange, Exchange? inputExchange) => _inner.OnCompletion(exchange, inputExchange);

        public void OnTimeout(Exchange exchange, int index, int total, TimeSpan timeout) => Timeouts.Add((index, total, timeout));
    }
}
