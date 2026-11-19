using Caravan.Builder;
using Caravan.Components.Scheduler;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests;

public class SchedulerTests : ContextTestBase
{
    private readonly TestTimeProvider _time = new();

    public SchedulerTests()
    {
        Context.TimeProvider = _time;
    }

    [Fact]
    public async Task Polls_after_the_initial_delay_then_after_each_delay()
    {
        await StartWithRoutes(b => b.From("scheduler:s?initialDelay=2000&delay=1000").To("mock:poll"));
        await _time.WaitForTimersAsync(1);
        var mock = Mock("poll");

        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(0, mock.ReceivedCounter);
        mock.ExpectedMessageCount(1);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(1));
        await mock.AssertIsSatisfiedAsync();

        mock.ExpectedMessageCount(3);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Default_options_follow_camel()
    {
        await Context.StartAsync();

        var endpoint = Context.GetEndpoint<SchedulerEndpoint>("scheduler:defaults");

        Assert.Equal(TimeSpan.FromSeconds(1), endpoint.InitialDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(500), endpoint.Delay);
        Assert.True(endpoint.UseFixedDelay);
        Assert.True(endpoint.StartScheduler);
        Assert.False(endpoint.Greedy);
    }

    [Fact]
    public async Task Repeat_count_stops_the_scheduler()
    {
        await StartWithRoutes(b => b.From("scheduler:s?initialDelay=0&delay=1000&repeatCount=2").RouteId("r").To("mock:poll"));
        var mock = Mock("poll");
        mock.ExpectedMessageCount(2);
        await _time.WaitForTimersAsync(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await mock.AssertIsSatisfiedAsync();
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, mock.ReceivedCounter);
        var consumer = (ScheduledPollConsumer)Context.GetRoute("r")!.Consumer!;
        await WaitUntil(() => !consumer.IsSchedulerStarted);
        Assert.Equal(2, consumer.Counter);
    }

    [Fact]
    public async Task Include_metadata_sets_name_and_fired_time()
    {
        await StartWithRoutes(b => b.From("scheduler:meta?initialDelay=0&includeMetadata=true").To("mock:poll"));
        var mock = Mock("poll");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();

        var exchange = mock.ReceivedExchanges[0];
        Assert.Equal("meta", exchange.GetProperty(ExchangePropertyKeys.TimerName));
        Assert.Equal(_time.Start, exchange.GetProperty(ExchangePropertyKeys.TimerFiredTime));
        Assert.Equal(_time.Start, exchange.Message.GetHeader(CaravanHeaders.FiredTime));
    }

    [Fact]
    public async Task Greedy_polls_again_while_messages_were_polled()
    {
        var calls = 0;
        await StartWithRoutes(b => b.From("scheduler:g?initialDelay=0&delay=1000&greedy=true")
            .Process(e => e.SetProperty(ExchangePropertyKeys.SchedulerPolledMessages, Interlocked.Increment(ref calls) < 3))
            .To("mock:poll"));
        await _time.WaitForTimersAsync(1);

        Assert.Equal(3, Mock("poll").ReceivedCounter);
    }

    [Fact]
    public async Task Backoff_skips_polls_after_idle_threshold()
    {
        await StartWithRoutes(b => b.From("scheduler:b?initialDelay=0&delay=1000&backoffMultiplier=2&backoffIdleThreshold=1")
            .Process(e => e.SetProperty(ExchangePropertyKeys.SchedulerPolledMessages, false))
            .To("mock:poll"));
        await _time.WaitForTimersAsync(1);
        var mock = Mock("poll");
        var counts = new List<int>();

        for (var i = 1; i <= 6; i++)
        {
            await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
            counts.Add(mock.ReceivedCounter);
        }

        // Polls at t=0, 3 and 6: after each idle poll, the next two scheduled polls are skipped.
        Assert.Equal([1, 1, 2, 2, 2, 3], counts);
    }

    [Fact]
    public async Task Backoff_requires_a_threshold()
    {
        await Assert.ThrowsAnyAsync<CaravanException>(() =>
            StartWithRoutes(b => b.From("scheduler:b?backoffMultiplier=2").To("mock:poll")));
    }

    [Fact]
    public async Task Start_scheduler_false_waits_for_manual_start()
    {
        await StartWithRoutes(b => b.From("scheduler:m?initialDelay=0&delay=1000&startScheduler=false").RouteId("r").To("mock:poll"));
        var consumer = (ScheduledPollConsumer)Context.GetRoute("r")!.Consumer!;
        Assert.False(consumer.IsSchedulerStarted);
        Assert.Equal(0, _time.TimersCreated);

        consumer.StartScheduling();

        Mock("poll").ExpectedMessageCount(1);
        await Mock("poll").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Fixed_rate_scheduling_ignores_poll_duration()
    {
        await StartWithRoutes(b => b.From("scheduler:r?initialDelay=0&delay=1000&useFixedDelay=false")
            .Process(_ => _time.Advance(TimeSpan.FromMilliseconds(400)))
            .To("mock:poll"));
        await _time.WaitForTimersAsync(1);
        var mock = Mock("poll");

        // First poll started at t=0 and ended at t=0.4s: the next one is due at t=1s, not t=1.4s.
        mock.ExpectedMessageCount(2);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(600));
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Suspended_scheduler_does_not_poll()
    {
        await StartWithRoutes(b => b.From("scheduler:s?initialDelay=0&delay=1000").RouteId("r").To("mock:poll"));
        await _time.WaitForTimersAsync(1);
        await Context.SuspendRouteAsync("r");

        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Mock("poll").ReceivedCounter);

        await Context.ResumeRouteAsync("r");
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, Mock("poll").ReceivedCounter);
    }

    [Fact]
    public async Task Poll_strategy_hooks_are_called()
    {
        var strategy = new RecordingStrategy();
        var component = new ScriptedPollComponent(n => n == 2 ? throw new InvalidOperationException("poll failed") : (int)n);
        Context.AddComponent("scripted", component);
        Context.Registry.Bind("strategy", strategy);
        await StartWithRoutes(b => b.From("scripted:x?initialDelay=0&delay=1000&pollStrategy=#strategy").To("mock:poll"));
        await _time.WaitForTimersAsync(1);

        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["begin", "commit 1", "begin", "rollback 0 poll failed"], strategy.Calls);
    }

    [Fact]
    public async Task Rollback_returning_true_retries_immediately()
    {
        var strategy = new RecordingStrategy { RetryOnce = true };
        var component = new ScriptedPollComponent(n => n == 1 ? throw new InvalidOperationException("first") : 5);
        Context.AddComponent("scripted", component);
        Context.Registry.Bind("strategy", strategy);
        await StartWithRoutes(b => b.From("scripted:x?initialDelay=0&delay=1000&pollStrategy=#strategy").To("mock:poll"));
        await _time.WaitForTimersAsync(1);

        Assert.Equal(["begin", "rollback 0 first", "begin", "commit 5"], strategy.Calls);
    }

    [Fact]
    public async Task Begin_returning_false_skips_the_poll()
    {
        var strategy = new RecordingStrategy { Begin = false };
        var component = new ScriptedPollComponent(n => 1);
        Context.AddComponent("scripted", component);
        Context.Registry.Bind("strategy", strategy);
        await StartWithRoutes(b => b.From("scripted:x?initialDelay=0&delay=1000&pollStrategy=#strategy").To("mock:poll"));
        await _time.WaitForTimersAsync(1);

        Assert.Equal(0, component.Polls);
        Assert.Equal(["begin", "commit 0"], strategy.Calls);
    }

    [Fact]
    public async Task Error_backoff_skips_polls_after_failures()
    {
        var component = new ScriptedPollComponent(_ => throw new InvalidOperationException("down"));
        Context.AddComponent("scripted", component);
        await StartWithRoutes(b => b.From("scripted:x?initialDelay=0&delay=1000&backoffMultiplier=1&backoffErrorThreshold=2").To("mock:poll"));
        await _time.WaitForTimersAsync(1);

        var polls = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
            polls.Add(component.Polls);
        }

        // Polls at t=0 and 1 fail (threshold 2), t=2 is skipped, t=3 and 4 poll, t=5 is skipped.
        Assert.Equal([2, 2, 3, 4, 4], polls);
    }

    [Fact]
    public async Task Send_empty_message_when_idle()
    {
        var component = new ScriptedPollComponent(_ => 0);
        Context.AddComponent("scripted", component);
        await StartWithRoutes(b => b.From("scripted:x?initialDelay=0&delay=1000&sendEmptyMessageWhenIdle=true").To("mock:poll"));
        Mock("poll").ExpectedMessageCount(1);

        await Mock("poll").AssertIsSatisfiedAsync();
        Assert.Null(Mock("poll").ReceivedExchanges[0].Message.Body);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestHelpers.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Yield();
        }
    }

    private sealed class RecordingStrategy : IPollingConsumerPollStrategy
    {
        public List<string> Calls { get; } = [];

        public bool Begin { get; init; } = true;

        public bool RetryOnce { get; set; }

        public ValueTask<bool> BeginAsync(IConsumer consumer, IEndpoint endpoint)
        {
            Calls.Add("begin");
            return ValueTask.FromResult(Begin);
        }

        public ValueTask CommitAsync(IConsumer consumer, IEndpoint endpoint, int polledMessages)
        {
            Calls.Add($"commit {polledMessages}");
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> RollbackAsync(IConsumer consumer, IEndpoint endpoint, int retryCounter, Exception cause)
        {
            Calls.Add($"rollback {retryCounter} {cause.Message}");
            var retry = RetryOnce;
            RetryOnce = false;
            return ValueTask.FromResult(retry);
        }
    }

    private sealed class ScriptedPollComponent(Func<long, int> script) : ComponentBase
    {
        private long _polls;

        public int Poll() => script(Interlocked.Increment(ref _polls));

        public long Polls => Interlocked.Read(ref _polls);

        protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
            new ScriptedPollEndpoint(uri, this);

        private sealed class ScriptedPollEndpoint(string uri, ScriptedPollComponent component)
            : ScheduledPollEndpoint(uri, component, component.Context!)
        {
            public override IProducer CreateProducer() => throw new NotSupportedException();

            public override IConsumer CreateConsumer(IProcessor processor)
            {
                var consumer = new ScriptedPollConsumer(this, processor, component);
                ConfigureConsumer(consumer);
                return consumer;
            }
        }

        private sealed class ScriptedPollConsumer(IEndpoint endpoint, IProcessor processor, ScriptedPollComponent component)
            : ScheduledPollConsumer(endpoint, processor)
        {
            protected override Task<int> PollAsync(CancellationToken cancellationToken) =>
                Task.FromResult(component.Poll());
        }
    }
}
