using Caravan.Builder;
using Caravan.Components.Seda;
using Caravan.Impl;
using Caravan.Spi;
using static Caravan.Core.Tests.TestHelpers;

namespace Caravan.Core.Tests;

public class SedaTests : ContextTestBase
{
    [Fact]
    public async Task InOnly_send_returns_before_the_consumer_processes()
    {
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:in").Process(async _ => await gate.Task).To("mock:result"));
        Mock("result").ExpectedBodiesReceived("a");

        await Template.SendBodyAsync("seda:in", "a").Bounded();

        Assert.Equal(0, Mock("result").ReceivedCounter);
        gate.SetResult();
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Consumer_gets_a_correlated_copy_from_the_seda_endpoint()
    {
        await StartWithRoutes(b => b.From("seda:in").RouteId("consumer").To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        var sent = await Template.SendAsync("seda:in", e => e.Message.Body = "x");

        await mock.AssertIsSatisfiedAsync();
        var received = mock.ReceivedExchanges[0];
        Assert.NotEqual(sent.ExchangeId, received.ExchangeId);
        Assert.Equal(sent.ExchangeId, received.GetProperty(ExchangePropertyKeys.CorrelationId));
        Assert.Equal("consumer", received.FromRouteId);
        Assert.IsType<SedaEndpoint>(received.FromEndpoint);
    }

    [Fact]
    public async Task Concurrent_consumers_process_in_parallel()
    {
        var entered = 0;
        var allIn = NewSignal();
        await StartWithRoutes(b => b.From("seda:in?concurrentConsumers=3").Process(async _ =>
        {
            if (Interlocked.Increment(ref entered) == 3)
            {
                allIn.SetResult();
            }

            await allIn.Task;
        }).To("mock:result"));
        Mock("result").ExpectedMessageCount(3);

        for (var i = 0; i < 3; i++)
        {
            await Template.SendBodyAsync("seda:in", i);
        }

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Single_consumer_processes_in_order()
    {
        await StartWithRoutes(b => b.From("seda:in").To("mock:result"));
        var bodies = Enumerable.Range(0, 20).Cast<object?>().ToArray();
        Mock("result").ExpectedBodiesReceived(bodies);

        foreach (var body in bodies)
        {
            await Template.SendBodyAsync("seda:in", body);
        }

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Endpoints_with_the_same_name_share_the_queue()
    {
        await Context.StartAsync();
        var a = Context.GetEndpoint<SedaEndpoint>("seda:q?size=5");
        var b = Context.GetEndpoint<SedaEndpoint>("seda:q?concurrentConsumers=2");

        Assert.NotSame(a, b);
        Assert.Equal(5, b.Size);
        await Template.SendBodyAsync("seda:q?size=5", "x");
        Assert.Equal(1, b.CurrentQueueSize);
    }

    [Fact]
    public async Task Different_size_for_an_existing_queue_fails()
    {
        await Context.StartAsync();
        Context.GetEndpoint("seda:q?size=5");

        var e = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("seda:q?size=10"));
        Assert.Contains("does not match given queue size 10", e.Message);
    }

    [Fact]
    public async Task Default_size_is_1000_and_zero_is_unbounded()
    {
        await Context.StartAsync();
        Assert.Equal(1000, Context.GetEndpoint<SedaEndpoint>("seda:a").Size);

        for (var i = 0; i < 1500; i++)
        {
            await Template.SendBodyAsync("seda:b?size=0", i);
        }

        Assert.Equal(1500, Context.GetEndpoint<SedaEndpoint>("seda:b").CurrentQueueSize);
    }

    [Fact]
    public async Task Full_queue_fails_by_default()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:q?size=1", "a");

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("seda:q?size=1", "b"));

        var inner = Assert.IsType<InvalidOperationException>(e.InnerException);
        Assert.Contains("Queue full", inner.Message);
    }

    [Fact]
    public async Task Discard_when_full_drops_silently()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:q?size=1&discardWhenFull=true", "a");
        await Template.SendBodyAsync("seda:q?size=1&discardWhenFull=true", "b");

        await using var consumer = Context.CreateConsumerTemplate();
        Assert.Equal("a", await consumer.ReceiveBodyNoWaitAsync<string>("seda:q"));
        Assert.Null(await consumer.ReceiveBodyNoWaitAsync<string>("seda:q"));
    }

    [Fact]
    public async Task Block_when_full_waits_for_room()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:q?size=1&blockWhenFull=true", "a");

        var blocked = Template.SendBodyAsync("seda:q?size=1&blockWhenFull=true", "b");
        Assert.False(blocked.IsCompleted);

        await using var consumer = Context.CreateConsumerTemplate();
        Assert.Equal("a", await consumer.ReceiveBodyAsync<string>("seda:q", Wait));
        await blocked.Bounded();
        Assert.Equal("b", await consumer.ReceiveBodyAsync<string>("seda:q", Wait));
    }

    [Fact]
    public async Task Block_when_full_with_offer_timeout_times_out()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await Context.StartAsync();
        const string uri = "seda:q?size=1&blockWhenFull=true&offerTimeout=2000";
        await Template.SendBodyAsync(uri, "a");

        var timers = time.TimersCreated;
        var blocked = Template.SendBodyAsync(uri, "b");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(2));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => blocked.Bounded());
        Assert.IsType<ExchangeTimedOutException>(e.InnerException);
        Assert.Equal(1, Context.GetEndpoint<SedaEndpoint>(uri).CurrentQueueSize);
    }

    [Fact]
    public async Task Request_reply_waits_and_copies_the_result_back()
    {
        await StartWithRoutes(b => b.From("seda:in").SetBody(e => "Hello " + e.Message.Body).SetHeader("handled", _ => true));

        var reply = await Template.SendAsync("seda:in", e => e.Message.Body = "World", ExchangePattern.InOut);

        Assert.Null(reply.Exception);
        Assert.Equal("Hello World", reply.Message.Body);
        Assert.Equal(true, reply.Message.GetHeader("handled"));
    }

    [Fact]
    public async Task Request_reply_propagates_the_consumer_failure()
    {
        await StartWithRoutes(b => b.From("seda:in").Process(_ => throw new InvalidOperationException("boom")));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("seda:in", "x"));

        Assert.Equal("boom", e.InnerException!.Message);
    }

    [Fact]
    public async Task Wait_always_blocks_InOnly_senders_until_done()
    {
        var done = false;
        await StartWithRoutes(b => b.From("seda:in?waitForTaskToComplete=Always").Process(async _ =>
        {
            await Task.Yield();
            done = true;
        }));

        await Template.SendBodyAsync("seda:in?waitForTaskToComplete=Always", "x");

        Assert.True(done);
    }

    [Fact]
    public async Task Wait_never_does_not_wait_for_a_reply()
    {
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:in").Process(async _ => await gate.Task).SetBody(_ => "reply"));

        var result = await Template.SendAsync("seda:in?waitForTaskToComplete=Never", e => e.Message.Body = "request", ExchangePattern.InOut).Bounded();

        Assert.Equal("request", result.Message.Body);
        gate.SetResult();
    }

    [Fact]
    public async Task Async_wait_property_overrides_the_endpoint_option()
    {
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:in").Process(async _ => await gate.Task).SetBody(_ => "reply"));

        var result = await Template.SendAsync(
            "seda:in",
            e =>
            {
                e.Message.Body = "request";
                e.SetProperty(ExchangePropertyKeys.AsyncWait, WaitForTaskToComplete.Never);
            },
            ExchangePattern.InOut).Bounded();

        Assert.Equal("request", result.Message.Body);
        gate.SetResult();
    }

    [Fact]
    public async Task Request_times_out_and_the_queued_copy_is_skipped()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await StartWithRoutes(b => b.From("seda:in").RouteId("consumer").AutoStartup(false).To("mock:result"));

        var timers = time.TimersCreated;
        var request = Template.SendAsync("seda:in?timeout=5000", e => e.Message.Body = "late", ExchangePattern.InOut);
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(5));
        var result = await request.Bounded();

        var timedOut = Assert.IsType<ExchangeTimedOutException>(result.Exception);
        Assert.Equal(TimeSpan.FromSeconds(5), timedOut.Timeout);

        Mock("result").ExpectedBodiesReceived("next");
        await Template.SendBodyAsync("seda:in", "next");
        await Context.StartRouteAsync("consumer");
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Fail_if_no_consumers()
    {
        await Context.StartAsync();

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("seda:none?failIfNoConsumers=true", "x"));

        Assert.IsType<SedaConsumerNotAvailableException>(e.InnerException);
    }

    [Fact]
    public async Task Discard_if_no_consumers()
    {
        await Context.StartAsync();

        await Template.SendBodyAsync("seda:none?discardIfNoConsumers=true", "x");

        Assert.Equal(0, Context.GetEndpoint<SedaEndpoint>("seda:none").CurrentQueueSize);
    }

    [Fact]
    public async Task Multiple_consumers_each_get_a_copy()
    {
        await StartWithRoutes(b =>
        {
            b.From("seda:topic?multipleConsumers=true").To("mock:a");
            b.From("seda:topic?multipleConsumers=true").To("mock:b");
        });
        Mock("a").ExpectedBodiesReceived("1", "2");
        Mock("b").ExpectedBodiesReceived("1", "2");

        await Template.SendBodyAsync("seda:topic?multipleConsumers=true", "1");
        await Template.SendBodyAsync("seda:topic?multipleConsumers=true", "2");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Competing_consumers_share_the_messages()
    {
        await StartWithRoutes(b =>
        {
            b.From("seda:work").To("mock:all");
            b.From("seda:work").To("mock:all");
        });
        Mock("all").ExpectedMessageCount(10);

        for (var i = 0; i < 10; i++)
        {
            await Template.SendBodyAsync("seda:work", i);
        }

        await Mock("all").AssertIsSatisfiedAsync();
        Assert.Equal(Enumerable.Range(0, 10), Mock("all").GetReceivedBodies<int>().Order());
    }

    [Fact]
    public async Task InOnly_hands_over_completion_callbacks_to_the_queued_copy()
    {
        var sync = new RecordingSynchronization();
        var gate = NewSignal();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Process(e => e.UnitOfWork!.AddSynchronization(sync)).To("seda:next");
            b.From("seda:next").Process(async _ => await gate.Task).SetBody(_ => "processed");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.False(sync.Completed.Task.IsCompleted);
        gate.SetResult();
        var completed = await sync.Completed.Task.Bounded();
        Assert.Equal("processed", completed.Message.Body);
        Assert.True(sync.Succeeded);
    }

    [Fact]
    public async Task Request_reply_keeps_completion_callbacks_on_the_caller()
    {
        var sync = new RecordingSynchronization();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Process(e => e.UnitOfWork!.AddSynchronization(sync)).To("seda:next").To("mock:result");
            b.From("seda:next").SetBody(_ => "reply");
        });
        Mock("result").ExpectedBodiesReceived("reply");

        var result = await Template.RequestBodyAsync("direct:start", "x");

        Assert.Equal("reply", result);
        var completed = await sync.Completed.Task.Bounded();
        Assert.Equal("direct:start", completed.FromEndpoint!.EndpointUri);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Suspended_route_keeps_messages_queued_until_resumed()
    {
        await StartWithRoutes(b => b.From("seda:in").RouteId("r").To("mock:result"));
        await Context.SuspendRouteAsync("r");
        Assert.Equal(ServiceStatus.Suspended, Context.GetRouteStatus("r"));

        await Template.SendBodyAsync("seda:in", "a");
        await Template.SendBodyAsync("seda:in", "b");
        Assert.Equal(2, Context.GetEndpoint<SedaEndpoint>("seda:in").CurrentQueueSize);
        Assert.Equal(0, Mock("result").ReceivedCounter);

        Mock("result").ExpectedBodiesReceived("a", "b");
        await Context.ResumeRouteAsync("r");
        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("r"));
    }

    [Fact]
    public async Task Suspend_waits_for_the_exchange_being_processed()
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:in").RouteId("r").Process(async _ =>
        {
            entered.TrySetResult();
            await gate.Task;
        }).To("mock:result"));
        await Template.SendBodyAsync("seda:in", "a");
        await entered.Task.Bounded();

        var suspend = Context.SuspendRouteAsync("r");
        await Template.SendBodyAsync("seda:in", "b");
        Assert.False(suspend.IsCompleted);
        gate.SetResult();
        await suspend.Bounded();

        Assert.Equal(1, Mock("result").ReceivedCounter);
        Assert.Equal(1, Context.GetEndpoint<SedaEndpoint>("seda:in").CurrentQueueSize);
    }

    [Fact]
    public async Task Stopping_the_route_waits_for_inflight_exchanges()
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:in").RouteId("r").Process(async _ =>
        {
            entered.TrySetResult();
            await gate.Task;
        }).To("mock:result"));
        await Template.SendBodyAsync("seda:in", "a");
        await entered.Task.Bounded();

        var stop = Context.StopRouteAsync("r");
        Assert.False(stop.IsCompleted);
        gate.SetResult();
        await stop.Bounded();

        Assert.Equal(1, Mock("result").ReceivedCounter);
        Assert.Equal(ServiceStatus.Stopped, Context.GetRouteStatus("r"));
    }

    [Fact]
    public async Task Purge_when_stopping_clears_the_queue()
    {
        await StartWithRoutes(b => b.From("seda:in?purgeWhenStopping=true").RouteId("r").AutoStartup(false).To("mock:result"));
        await Template.SendBodyAsync("seda:in", "a");
        await Context.StartRouteAsync("r");
        await Context.SuspendRouteAsync("r");
        await Template.SendBodyAsync("seda:in", "b");

        await Context.StopRouteAsync("r");

        Assert.Equal(0, Context.GetEndpoint<SedaEndpoint>("seda:in").CurrentQueueSize);
    }

    [Fact]
    public async Task Consumer_polls_the_queue_directly()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:q", "a");
        await Template.SendBodyAsync("seda:q", "b");
        await using var consumer = Context.CreateConsumerTemplate();

        Assert.Equal("a", await consumer.ReceiveBodyAsync<string>("seda:q"));
        Assert.Equal("b", await consumer.ReceiveBodyAsync("seda:q", Wait));
        Assert.Null(await consumer.ReceiveNoWaitAsync("seda:q"));
    }

    [Fact]
    public async Task Request_reply_through_a_polling_consumer_completes_on_done_uow()
    {
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        var request = Template.RequestBodyAsync<string>("seda:q", "ping");
        var exchange = await consumer.ReceiveAsync("seda:q").Bounded();
        exchange.Message.Body = "pong";
        Assert.False(request.IsCompleted);
        await consumer.DoneUoWAsync(exchange);

        Assert.Equal("pong", await request.Bounded());
    }

    [Fact]
    public void Concurrent_consumers_must_be_positive()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("seda:in?concurrentConsumers=0").To("mock:x")));

        Assert.ThrowsAny<CaravanException>(() => Context.StartAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Vm_queues_are_shared_across_contexts()
    {
        var name = "vm:shared-" + Guid.NewGuid().ToString("N");
        await StartWithRoutes(b => b.From(name).To("mock:result"));
        await using var other = new CaravanContext();
        await other.StartAsync();
        await using var otherTemplate = other.CreateProducerTemplate();
        Mock("result").ExpectedBodiesReceived("from other");

        await otherTemplate.SendBodyAsync(name, "from other");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Same(Context, Mock("result").ReceivedExchanges[0].Context);
    }

    [Fact]
    public async Task Vm_request_reply_across_contexts()
    {
        var name = "vm:rr-" + Guid.NewGuid().ToString("N");
        await StartWithRoutes(b => b.From(name).SetBody(e => $"{e.Message.Body} from {e.Context.Name}"));
        Context.Name = "server";
        await using var other = new CaravanContext();
        await other.StartAsync();
        await using var otherTemplate = other.CreateProducerTemplate();

        Assert.Equal("hi from server", await otherTemplate.RequestBodyAsync<string>(name, "hi"));
    }

    [Fact]
    public async Task Seda_queues_are_not_shared_across_contexts()
    {
        await StartWithRoutes(b => b.From("seda:local").To("mock:result"));
        await using var other = new CaravanContext();
        await other.StartAsync();
        await using var otherTemplate = other.CreateProducerTemplate();

        await otherTemplate.SendBodyAsync("seda:local", "x");

        Assert.Equal(1, other.GetEndpoint<SedaEndpoint>("seda:local").CurrentQueueSize);
    }
}
