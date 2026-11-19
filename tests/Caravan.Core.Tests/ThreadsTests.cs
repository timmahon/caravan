using Caravan.Builder;
using Caravan.Model;
using Caravan.Processors;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>The <c>threads</c> EIP: a bounded worker pool for the rest of the route or a block.</summary>
public class ThreadsTests : ContextTestBase
{
    [Fact]
    public async Task Chained_threads_runs_the_rest_of_the_route_on_a_worker_and_the_caller_gets_the_result()
    {
        int? callerThread = null;
        int? workerThread = null;
        await StartWithRoutes(b => b.From("direct:start")
            .Process(_ => callerThread = Environment.CurrentManagedThreadId)
            .Threads(poolSize: 2)
            .Process(_ => workerThread = Environment.CurrentManagedThreadId)
            .SetBody(Simple("done ${body}"))
            .To("mock:result"));

        var reply = await Template.RequestBodyAsync("direct:start", "x");

        Assert.Equal("done x", reply);
        Assert.Equal(["done x"], Mock("result").ReceivedBodies);
        Assert.NotNull(workerThread);
        var route = Context.RouteDefinitions.Single();
        var threads = Assert.IsType<ThreadsDefinition>(route.Outputs[1]);
        Assert.Empty(threads.Outputs);
        Assert.Equal(5, route.AllNodes().Count());
    }

    [Fact]
    public async Task At_most_max_pool_size_exchanges_run_at_once_and_the_others_queue()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("seda:in?concurrentConsumers=10")
            .Threads(poolSize: 2, maxPoolSize: 2, maxQueueSize: -1)
            .Process(async _ => await probe.EnterAsync())
            .To("mock:result"));
        Mock("result").ExpectedMessageCount(6);

        for (var i = 0; i < 6; i++)
        {
            await Template.SendBodyAsync("seda:in", i);
        }

        var processor = FindThreads();
        await TestHelpers.WaitUntilAsync(() => processor.QueueSize == 4);
        await probe.WaitForStartedAsync(2);
        Assert.Equal(2, processor.ActiveCount);
        probe.Release(6);

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(2, probe.Max);
        await TestHelpers.WaitUntilAsync(() => processor.ActiveCount == 0);
    }

    [Fact]
    public async Task Extra_workers_start_when_the_queue_is_full_and_abort_rejects_beyond_max_pool_size()
    {
        var gate = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("seda:in?concurrentConsumers=10")
            .Threads(t => t.Process(async _ => await gate.Task).To("mock:result"), poolSize: 1, maxPoolSize: 2, maxQueueSize: 1, rejectedPolicy: ThreadPoolRejectedPolicy.Abort)
            .To("mock:after"));
        Mock("after").ExpectedMessageCount(3);

        for (var i = 0; i < 3; i++)
        {
            await Template.SendBodyAsync("seda:in", i);
        }

        var processor = FindThreads();
        await TestHelpers.WaitUntilAsync(() => processor.ActiveCount == 2 && processor.QueueSize == 1);
        var fourth = await SendDirectlyAsync(processor, 4);
        Assert.IsType<RejectedExecutionException>(fourth.Exception);

        gate.SetResult();
        await Mock("after").AssertIsSatisfiedAsync();
        Assert.Equal(3, Mock("result").ReceivedExchanges.Count);
    }

    [Fact]
    public async Task Caller_runs_processes_the_exchange_on_the_caller_when_full()
    {
        var gate = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start")
            .Threads(poolSize: 1, maxPoolSize: 1, maxQueueSize: 0, rejectedPolicy: ThreadPoolRejectedPolicy.CallerRuns)
            .Process(async e =>
            {
                if (e.Message.Body is "slow")
                {
                    await gate.Task;
                }
            })
            .To("mock:result"));

        var slow = Template.SendBodyAsync("direct:start", "slow");
        var processor = FindThreads();
        await TestHelpers.WaitUntilAsync(() => processor.ActiveCount == 1);

        await Template.SendBodyAsync("direct:start", "fast").Bounded();
        Assert.Equal(["fast"], Mock("result").ReceivedBodies);

        gate.SetResult();
        await slow.Bounded();
        Assert.Equal(["fast", "slow"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Failures_after_threads_reach_the_error_handler_and_the_caller()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:error"));
            b.From("direct:start").Threads().Process(_ => throw new InvalidOperationException("boom")).To("mock:result");
            b.From("direct:unhandled").Threads().Process(_ => throw new ArgumentException("bad"));
        });

        await Template.SendBodyAsync("direct:start", "x");
        var failed = await Template.SendAsync("direct:unhandled", _ => { });

        Assert.Single(Mock("error").ReceivedExchanges);
        Assert.Empty(Mock("result").ReceivedExchanges);
        Assert.IsType<ArgumentException>(failed.Exception);
    }

    [Fact]
    public async Task Invalid_pool_sizes_fail_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").Threads(poolSize: 5, maxPoolSize: 2).To("mock:result")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
        Assert.Contains("poolSize <= maxPoolSize", e.Message);
    }

    [Fact]
    public async Task Queued_exchanges_fail_when_the_pool_stops()
    {
        var gate = TestHelpers.NewSignal();
        var processor = new ThreadsProcessor(new Spi.DelegateProcessor(async _ => await gate.Task), 1, 1, 10, ThreadPoolRejectedPolicy.Abort, "p");
        await processor.StartAsync();
        var first = new Exchange(Context);
        var second = new Exchange(Context);
        var running = processor.ProcessAsync(first).AsTask();
        var queued = processor.ProcessAsync(second).AsTask();
        await TestHelpers.WaitUntilAsync(() => processor.QueueSize == 1);

        await processor.StopAsync();
        await queued.Bounded();
        Assert.IsType<RejectedExecutionException>(second.Exception);
        gate.SetResult();
        await running.Bounded();
        Assert.Null(first.Exception);
        await Assert.ThrowsAsync<RejectedExecutionException>(async () => await processor.ProcessAsync(new Exchange(Context)));
    }

    private ThreadsProcessor FindThreads()
    {
        var route = (Impl.Route)Context.Routes.Single();
        return route.RouteContext.Services.OfType<ThreadsProcessor>().Single();
    }

    private async Task<Exchange> SendDirectlyAsync(ThreadsProcessor processor, object body)
    {
        var exchange = new Exchange(Context);
        exchange.Message.Body = body;
        try
        {
            await processor.ProcessAsync(exchange);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }

        return exchange;
    }
}
