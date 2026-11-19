using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>delay semantics (mirrors Camel's DelayerTest), on a fake clock.</summary>
public class DelayTests : ContextTestBase
{
    private TestTimeProvider UseFakeTime()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        return time;
    }

    [Fact]
    public async Task Delays_the_exchange_on_the_context_clock()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Delay(TimeSpan.FromSeconds(1)).To("mock:result"));
        Mock("result").ExpectedMessageCount(1);

        var timers = time.TimersCreated;
        var sending = Template.SendBodyAsync("direct:start", "x");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, Mock("result").ReceivedCounter);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await sending.Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Delay_from_an_expression_in_milliseconds_or_as_a_time_span()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b =>
        {
            b.From("direct:header").Delay(Expr.Header("delay")).To("mock:result");
            b.From("direct:func").Delay(e => e.Message.Body).To("mock:result");
        });
        Mock("result").ExpectedBodiesReceived(250, "span");

        var timers = time.TimersCreated;
        var first = Template.SendBodyAndHeaderAsync("direct:header", 250, "delay", "250");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromMilliseconds(250));
        await first.Bounded();

        timers = time.TimersCreated;
        var second = Template.SendAsync("direct:func", e => e.Message.Body = TimeSpan.FromSeconds(2));
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(2));
        var result = await second.Bounded();

        Assert.Null(result.Exception);
        Assert.Equal(2, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Null_zero_or_negative_delays_do_not_wait()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Delay(Expr.Header("delay")).To("mock:result"));
        Mock("result").ExpectedMessageCount(3);

        await Template.SendBodyAsync("direct:start", "none");
        await Template.SendBodyAndHeaderAsync("direct:start", "zero", "delay", 0);
        await Template.SendBodyAndHeaderAsync("direct:start", "negative", "delay", -5);

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(0, time.TimersCreated);
    }

    [Fact]
    public async Task Synchronous_delay_blocks_the_calling_thread()
    {
        var time = UseFakeTime();
        int? threadBefore = null;
        int? threadAfter = null;
        await StartWithRoutes(b => b.From("direct:start")
            .Process(_ => threadBefore = Environment.CurrentManagedThreadId)
            .Delay(TimeSpan.FromSeconds(1), asyncDelayed: false)
            .Process(_ => threadAfter = Environment.CurrentManagedThreadId)
            .To("mock:result"));
        Mock("result").ExpectedMessageCount(1);

        var timers = time.TimersCreated;
        var sending = Task.Run(() => Template.SendBodyAsync("direct:start", "x"));
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await sending.Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(threadBefore, threadAfter);
    }

    [Fact]
    public async Task Cancellation_ends_the_delay_and_fails_the_exchange()
    {
        var time = UseFakeTime();
        using var cancel = new CancellationTokenSource();
        await StartWithRoutes(b => b.From("direct:start").Delay(TimeSpan.FromHours(1)).To("mock:result"));
        ExpectNone("result");

        var timers = time.TimersCreated;
        var sending = Template.SendAsync("direct:start", new Exchange(Context) { CancellationToken = cancel.Token });
        await time.WaitForTimersAsync(timers + 1);
        await cancel.CancelAsync();
        var result = await sending.Bounded();

        Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
        await Mock("result").AssertIsSatisfiedAsync();
    }
}
