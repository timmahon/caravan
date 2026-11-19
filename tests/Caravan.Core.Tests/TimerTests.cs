using Caravan.Builder;
using Caravan.Components.Timer;
using static Caravan.Core.Tests.TestHelpers;

namespace Caravan.Core.Tests;

public class TimerTests : ContextTestBase
{
    private readonly TestTimeProvider _time = new();

    public TimerTests()
    {
        Context.TimeProvider = _time;
    }

    private async Task StartTimerRoute(string uri, string routeId = "timer")
    {
        await StartWithRoutes(b => b.From(uri).RouteId(routeId).To("mock:tick"));
        await _time.WaitForTimersAsync(1);
    }

    [Fact]
    public async Task Fires_after_the_delay_then_every_period()
    {
        await StartTimerRoute("timer:t?delay=500&period=1000");
        var mock = Mock("tick");

        Assert.Equal(0, mock.ReceivedCounter);
        mock.ExpectedMessageCount(1);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(500));
        await mock.AssertIsSatisfiedAsync();

        mock.ExpectedMessageCount(3);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Does_not_fire_before_the_period_elapsed()
    {
        await StartTimerRoute("timer:t?delay=0&period=1000");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();

        _time.Advance(TimeSpan.FromMilliseconds(999));

        Assert.Equal(1, mock.ReceivedCounter);
    }

    [Fact]
    public async Task Repeat_count_limits_the_fires()
    {
        await StartTimerRoute("timer:t?delay=1000&period=1000&repeatCount=2");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(2);

        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        var timers = _time.TimersCreated;
        _time.Advance(TimeSpan.FromSeconds(1));
        await mock.AssertIsSatisfiedAsync();
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, mock.ReceivedCounter);
        Assert.Equal(timers, _time.TimersCreated);
    }

    [Fact]
    public async Task Zero_period_fires_once()
    {
        await StartTimerRoute("timer:once?delay=100&period=0");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(1);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        await mock.AssertIsSatisfiedAsync();
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(1, mock.ReceivedCounter);
    }

    [Fact]
    public async Task Include_metadata_sets_the_timer_properties_and_headers()
    {
        await StartTimerRoute("timer:meta?delay=0&period=2000&includeMetadata=true");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();
        mock.ExpectedMessageCount(2);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(2));
        await mock.AssertIsSatisfiedAsync();

        var first = mock.ReceivedExchanges[0];
        var second = mock.ReceivedExchanges[1];
        Assert.Equal("meta", first.GetProperty(ExchangePropertyKeys.TimerName));
        Assert.Equal(2000L, first.GetProperty(ExchangePropertyKeys.TimerPeriod));
        Assert.Equal(1L, first.GetProperty(ExchangePropertyKeys.TimerCounter));
        Assert.Equal(2L, second.GetProperty(ExchangePropertyKeys.TimerCounter));
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(start, first.GetProperty(ExchangePropertyKeys.TimerFiredTime));
        Assert.Equal(start.AddSeconds(2), second.Message.GetHeader(CaravanHeaders.FiredTime));
        Assert.Equal(start.AddSeconds(2).ToUnixTimeMilliseconds(), second.Message.GetHeader(CaravanHeaders.MessageTimestamp));
        Assert.Equal(ExchangePattern.InOnly, first.Pattern);
        Assert.Null(first.Message.Body);
    }

    [Fact]
    public async Task Metadata_is_not_set_by_default()
    {
        await StartTimerRoute("timer:plain?delay=0");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();

        Assert.Null(mock.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.TimerName));
    }

    [Fact]
    public async Task Fixed_delay_schedules_from_the_start_of_the_previous_fire()
    {
        var release = NewSignal();
        await StartWithRoutes(b => b.From("timer:slow?delay=0&period=1000").Process(async _ => await release.Task).To("mock:tick"));
        await _time.WaitForTimersAsync(1);

        // The fire at t=0 is still running at t=1s; the next one runs right after it, then one per period.
        _time.Advance(TimeSpan.FromSeconds(1));
        var timers = _time.TimersCreated;
        release.SetResult();
        await _time.WaitForTimersAsync(timers + 1);
        Mock("tick").ExpectedMessageCount(2);
        await Mock("tick").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Fixed_rate_catches_up_on_missed_fires()
    {
        var release = NewSignal();
        var calls = 0;
        await StartWithRoutes(b => b.From("timer:rate?delay=0&period=1000&fixedRate=true").Process(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await release.Task;
            }
        }).To("mock:tick"));
        await _time.WaitForTimersAsync(1);

        // While the first fire (t=0) blocks, the clock reaches t=3s: the fires due at 1s, 2s and 3s run back to back.
        _time.Advance(TimeSpan.FromSeconds(3));
        var mock = Mock("tick");
        mock.ExpectedMessageCount(4);
        release.SetResult();
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Suspended_timer_skips_fires_and_resumes()
    {
        await StartTimerRoute("timer:s?delay=0&period=1000&includeMetadata=true", "r");
        var mock = Mock("tick");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();

        await Context.SuspendRouteAsync("r");
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, mock.ReceivedCounter);

        await Context.ResumeRouteAsync("r");
        mock.ExpectedMessageCount(2);
        await _time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(2L, mock.ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.TimerCounter));
    }

    [Fact]
    public async Task Stopped_timer_no_longer_fires()
    {
        await StartTimerRoute("timer:stop?delay=0&period=1000", "r");
        Mock("tick").ExpectedMessageCount(1);
        await Mock("tick").AssertIsSatisfiedAsync();

        await Context.StopRouteAsync("r");
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Mock("tick").ReceivedCounter);
    }

    [Fact]
    public async Task Timer_endpoint_options_are_bound()
    {
        await Context.StartAsync();

        var endpoint = Context.GetEndpoint<TimerEndpoint>("timer:x?period=5s&delay=250&repeatCount=3&fixedRate=true");

        Assert.Equal("x", endpoint.TimerName);
        Assert.Equal(TimeSpan.FromSeconds(5), endpoint.Period);
        Assert.Equal(TimeSpan.FromMilliseconds(250), endpoint.Delay);
        Assert.Equal(3, endpoint.RepeatCount);
        Assert.True(endpoint.FixedRate);
        Assert.Throws<NotSupportedException>(() => endpoint.CreateProducer());
    }
}
