using Caravan.Builder;
using Caravan.Components.Direct;
using Caravan.Spi;
using Caravan.Support;
using static Caravan.Core.Tests.TestHelpers;

namespace Caravan.Core.Tests;

public class PollingConsumerTests : ContextTestBase
{
    [Fact]
    public async Task Any_endpoint_can_be_polled_through_the_event_driven_adapter()
    {
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        // The first call starts the direct consumer; messages sent afterwards are queued until received.
        Assert.Null(await consumer.ReceiveNoWaitAsync("direct:poll"));
        await Template.SendBodyAsync("direct:poll", "a");
        await Template.SendBodyAsync("direct:poll", "b");

        Assert.Equal("a", await consumer.ReceiveBodyAsync<string>("direct:poll").Bounded());
        Assert.Equal("b", await consumer.ReceiveBodyNoWaitAsync("direct:poll"));
    }

    [Fact]
    public async Task Factory_uses_the_endpoint_polling_consumer_or_the_adapter()
    {
        await Context.StartAsync();

        Assert.IsType<EventDrivenPollingConsumer>(PollingConsumerFactory.Create(Context.GetEndpoint("direct:x")));
        Assert.Equal("SedaPollingConsumer", PollingConsumerFactory.Create(Context.GetEndpoint("seda:x")).GetType().Name);
    }

    [Fact]
    public async Task Receive_with_timeout_returns_null_when_nothing_arrives()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        var receive = consumer.ReceiveAsync("seda:empty", TimeSpan.FromSeconds(3));
        await time.WaitForTimersAsync(1);
        Assert.False(receive.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Null(await receive.Bounded());
    }

    [Fact]
    public async Task Receive_with_timeout_returns_a_message_that_arrives_in_time()
    {
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        var receive = consumer.ReceiveBodyAsync<string>("seda:later", TimeSpan.FromMinutes(1));
        await Template.SendBodyAsync("seda:later", "hello");

        Assert.Equal("hello", await receive.Bounded());
    }

    [Fact]
    public async Task Timed_out_receive_does_not_lose_a_later_message()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        var receive = consumer.ReceiveAsync("seda:q", TimeSpan.FromSeconds(1));
        await time.WaitForTimersAsync(1);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await receive.Bounded());

        await Template.SendBodyAsync("seda:q", "kept");
        Assert.Equal("kept", await consumer.ReceiveBodyNoWaitAsync<string>("seda:q"));
    }

    [Fact]
    public async Task Receive_is_cancellable()
    {
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();
        using var cts = new CancellationTokenSource();

        var receive = consumer.ReceiveAsync("seda:never", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive.Bounded());
    }

    [Fact]
    public async Task Timer_can_be_polled()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();

        var receive = consumer.ReceiveAsync("timer:poll?delay=1000&includeMetadata=true");
        await time.WaitForTimersAsync(1);
        time.Advance(TimeSpan.FromSeconds(1));

        var exchange = await receive.Bounded();
        Assert.Equal("poll", exchange.GetProperty(ExchangePropertyKeys.TimerName));
    }

    [Fact]
    public async Task Done_uow_runs_handed_over_completions()
    {
        var sync = new RecordingSynchronization();
        await StartWithRoutes(b => b.From("direct:start").Process(e => e.UnitOfWork!.AddSynchronization(sync)).To("seda:out"));
        await using var consumer = Context.CreateConsumerTemplate();

        await Template.SendBodyAsync("direct:start", "x");
        var exchange = await consumer.ReceiveAsync("seda:out").Bounded();
        Assert.False(sync.Completed.Task.IsCompleted);

        await consumer.DoneUoWAsync(exchange);

        Assert.Same(exchange, await sync.Completed.Task.Bounded());
        Assert.True(sync.Succeeded);
    }

    [Fact]
    public async Task Receive_body_completes_the_unit_of_work()
    {
        var sync = new RecordingSynchronization();
        await StartWithRoutes(b => b.From("direct:start").Process(e => e.UnitOfWork!.AddSynchronization(sync)).To("seda:out"));
        await using var consumer = Context.CreateConsumerTemplate();

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal("x", await consumer.ReceiveBodyAsync<string>("seda:out").Bounded());
        Assert.True(sync.Completed.Task.IsCompleted);
    }

    [Fact]
    public async Task Receive_body_throws_when_the_exchange_failed()
    {
        await Context.StartAsync();
        await using var consumer = Context.CreateConsumerTemplate();
        Assert.Null(await consumer.ReceiveNoWaitAsync("direct:failed"));
        var failed = new Exchange(Context) { Exception = new InvalidOperationException("bad") };
        await Template.SendAsync("direct:failed", failed);

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => consumer.ReceiveBodyAsync("direct:failed").Bounded());
        Assert.Equal("bad", e.InnerException!.Message);
    }

    [Fact]
    public async Task Event_driven_consumer_fails_or_blocks_when_full()
    {
        await Context.StartAsync();
        var endpoint = Context.GetEndpoint("direct:full");
        var failing = new EventDrivenPollingConsumer(endpoint, queueSize: 1, blockWhenFull: false);
        await failing.StartAsync();
        await failing.ProcessAsync(new Exchange(Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ProcessAsync(new Exchange(Context)).AsTask());
        await failing.StopAsync();

        var blocking = new EventDrivenPollingConsumer(Context.GetEndpoint("direct:full2"), queueSize: 1);
        await blocking.StartAsync();
        await blocking.ProcessAsync(new Exchange(Context));
        var second = blocking.ProcessAsync(new Exchange(Context)).AsTask();
        Assert.False(second.IsCompleted);
        Assert.NotNull(blocking.ReceiveNoWait());
        await second.Bounded();
        Assert.Equal(1, blocking.Count);
        await blocking.StopAsync();
    }

    [Fact]
    public async Task Stopping_the_template_stops_the_polling_consumers()
    {
        await Context.StartAsync();
        var consumer = Context.CreateConsumerTemplate();
        Assert.Null(await consumer.ReceiveNoWaitAsync("direct:owned"));
        await Template.SendBodyAsync("direct:owned", "x");

        await consumer.DisposeAsync();

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:owned?block=false", "y"));
        Assert.IsType<DirectConsumerNotAvailableException>(e.InnerException);
    }
}
