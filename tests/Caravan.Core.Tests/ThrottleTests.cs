using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Model;
using Caravan.Processors;

namespace Caravan.Core.Tests;

/// <summary>throttle semantics in both modes (mirrors Camel's ThrottlerTest and ConcurrentRequestsThrottlerTest).</summary>
public class ThrottleTests : ContextTestBase
{
    private TestTimeProvider UseFakeTime()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        return time;
    }

    private ThrottleProcessor Throttler(string routeId = "r") =>
        ((Route)Context.GetRoute(routeId)!).RouteContext.Services.OfType<ThrottleProcessor>().Single();

    private Task[] SendConcurrently(string uri, int count, Func<int, string>? header = null) =>
        Enumerable.Range(0, count)
            .Select(i => Task.Run(() => Template.SendBodyAndHeaderAsync(uri, i, "key", header?.Invoke(i))))
            .ToArray();

    // ----- ConcurrentRequests -----

    [Fact]
    public async Task Default_mode_limits_concurrent_exchanges_until_they_complete()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(2).Process(_ => probe.EnterAsync()).To("mock:result"));
        Mock("result").ExpectedMessageCount(5);

        var sends = SendConcurrently("direct:start", 5);
        await probe.WaitForStartedAsync(2);
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 3);
        Assert.Equal(2, probe.StartedCount);
        Assert.Equal(2, Throttler().InFlight);

        probe.Release();
        await probe.WaitForStartedAsync(3);
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 2);
        probe.Release(4);
        await Task.WhenAll(sends).Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(2, probe.Max);
        Assert.Equal(0, Throttler().InFlight);
        Assert.Equal(ThrottlingMode.ConcurrentRequests, Throttler().Mode);
    }

    [Fact]
    public async Task Permit_is_held_until_the_exchange_completes_even_through_called_routes()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RouteId("r").Throttle(1).To("direct:work");
            b.From("direct:work").Process(_ => probe.EnterAsync()).To("mock:result");
        });

        var sends = SendConcurrently("direct:start", 3);
        await probe.WaitForStartedAsync(1);
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 2);
        probe.Release(3);
        await Task.WhenAll(sends).Bounded();

        Assert.Equal(1, probe.Max);
        Assert.Equal(3, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Concurrent_reject_execution_fails_exchanges_over_the_limit()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(1, t => t.RejectExecution()).Process(_ => probe.EnterAsync()));

        var first = Template.SendBodyAsync("direct:start", 1);
        await probe.WaitForStartedAsync(1);
        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 2));
        probe.Release();
        await first.Bounded();

        var rejected = Assert.IsType<ThrottlerRejectedExecutionException>(ex.InnerException);
        Assert.Contains("max concurrent requests of 1", rejected.Message);
        Assert.Equal(0, Throttler().InFlight);
    }

    [Fact]
    public async Task Failed_exchanges_return_their_permit()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(1, t => t.RejectExecution())
            .Filter((int n) => n % 2 == 0, f => f.ThrowException(new InvalidOperationException("even")))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived(1, 3);

        for (var i = 0; i < 4; i++)
        {
            var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = i);
            Assert.Equal(i % 2 == 0, exchange.Exception is InvalidOperationException);
        }

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(0, Throttler().InFlight);
    }

    [Fact]
    public async Task Concurrent_limit_per_correlation_key()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Throttle(1, t => t.CorrelationExpression(Expr.Header("key")))
            .Process(_ => probe.EnterAsync()));

        var sends = SendConcurrently("direct:start", 4, i => i < 2 ? "a" : "b");
        await probe.WaitForStartedAsync(2);
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 2);
        Assert.Equal(2, Throttler().KeyCount);
        probe.Release(4);
        await Task.WhenAll(sends).Bounded();

        Assert.Equal(2, probe.Max);
        Assert.Equal(0, Throttler().KeyCount);
    }

    [Fact]
    public async Task Max_requests_is_evaluated_per_exchange()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(Expr.Header("max")).Process(_ => probe.EnterAsync()));

        var first = Template.SendBodyAndHeaderAsync("direct:start", 1, "max", 1);
        await probe.WaitForStartedAsync(1);
        var second = Template.SendBodyAndHeaderAsync("direct:start", 2, "max", 1);
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 1);

        // A higher maximum lets the waiting exchange and the new one through.
        var third = Template.SendBodyAndHeaderAsync("direct:start", 3, "max", 3);
        await probe.WaitForStartedAsync(3);
        probe.Release(3);
        await Task.WhenAll(first, second, third).Bounded();

        Assert.Equal(3, probe.Max);
    }

    [Fact]
    public async Task Null_or_non_positive_max_requests_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Throttle(Expr.Header("max")).To("mock:result"));
        ExpectNone("result");

        var none = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));
        var zero = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:start", "x", "max", 0));

        Assert.Contains("evaluated as null", none.InnerException!.Message);
        Assert.Contains("positive", zero.InnerException!.Message);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Waiting_exchange_is_cancelled_with_its_token()
    {
        var probe = new ConcurrencyProbe();
        using var cancel = new CancellationTokenSource();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(1).Process(_ => probe.EnterAsync()));

        var first = Template.SendBodyAsync("direct:start", 1);
        await probe.WaitForStartedAsync(1);
        var waiting = Template.SendAsync("direct:start", new Exchange(Context) { CancellationToken = cancel.Token });
        await TestHelpers.WaitUntilAsync(() => Throttler().Waiting == 1);
        await cancel.CancelAsync();
        var cancelled = await waiting.Bounded();
        probe.Release();
        await first.Bounded();

        Assert.IsAssignableFrom<OperationCanceledException>(cancelled.Exception);
        Assert.Equal(0, Throttler().Waiting);
        Assert.Equal(0, Throttler().InFlight);
        Assert.Equal(1, probe.StartedCount);
    }

    // ----- TotalRequests -----

    [Fact]
    public async Task Time_period_mode_lets_max_requests_through_per_period()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").Throttle(3, t => t.TimePeriod(TimeSpan.FromSeconds(1))).To("mock:result"));

        var timers = time.TimersCreated;
        var sends = SendConcurrently("direct:start", 5);
        await time.WaitForTimersAsync(timers + 2);
        await TestHelpers.WaitUntilAsync(() => Mock("result").ReceivedCounter == 3);
        Assert.Equal(ThrottlingMode.TotalRequests, Throttler().Mode);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(3, Mock("result").ReceivedCounter);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(sends).Bounded();

        Assert.Equal(5, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Time_period_window_slides_from_each_admission()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Throttle(2, t => t.TimePeriod(TimeSpan.FromSeconds(1))).To("mock:result"));

        await Template.SendBodyAsync("direct:start", 1);
        time.Advance(TimeSpan.FromMilliseconds(600));
        await Template.SendBodyAsync("direct:start", 2);

        // Two admissions in the window: the third waits until the first is one period old (t = 1000 ms), not until t = 1600 ms.
        var timers = time.TimersCreated;
        var third = Template.SendBodyAsync("direct:start", 3);
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal(2, Mock("result").ReceivedCounter);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await third.Bounded();

        Assert.Equal(3, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Time_period_reject_execution()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Throttle(1, t => t.TimePeriod(TimeSpan.FromSeconds(10)).RejectExecution()).To("mock:result"));

        await Template.SendBodyAsync("direct:start", 1);
        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 2));
        time.Advance(TimeSpan.FromSeconds(10));
        await Template.SendBodyAsync("direct:start", 3);

        var rejected = Assert.IsType<ThrottlerRejectedExecutionException>(ex.InnerException);
        Assert.Contains("max throttle rate of 1 within 10000ms", rejected.Message);
        Assert.Equal([1, 3], Mock("result").GetReceivedBodies<int>());
    }

    [Fact]
    public async Task Time_period_per_correlation_key()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start")
            .Throttle(1, t => t.Mode(ThrottlingMode.TotalRequests).CorrelationExpression(e => e.Message.GetHeader("key")).RejectExecution())
            .To("mock:result"));

        await Template.SendBodyAndHeaderAsync("direct:start", 1, "key", "a");
        await Template.SendBodyAndHeaderAsync("direct:start", 2, "key", "b");
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:start", 3, "key", "a"));
        time.Advance(TimeSpan.FromSeconds(1));
        await Template.SendBodyAndHeaderAsync("direct:start", 4, "key", "a");

        Assert.Equal([1, 2, 4], Mock("result").GetReceivedBodies<int>());
    }

    [Fact]
    public async Task Time_period_synchronous_wait_blocks_the_thread()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Throttle(1, t => t.TimePeriod(TimeSpan.FromSeconds(1)).AsyncDelayed(false)).To("mock:result"));

        await Template.SendBodyAsync("direct:start", 1);
        var timers = time.TimersCreated;
        var second = Task.Run(() => Template.SendBodyAsync("direct:start", 2));
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.Bounded();

        Assert.Equal(2, Mock("result").ReceivedCounter);
    }
}
