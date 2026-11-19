using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>sample semantics (mirrors Camel's SamplingThrottlerTest), on a fake clock.</summary>
public class SampleTests : ContextTestBase
{
    [Fact]
    public async Task Lets_one_exchange_through_per_period()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await StartWithRoutes(b => b.From("direct:start").Sample(TimeSpan.FromSeconds(1)).To("mock:result"));
        Mock("result").ExpectedBodiesReceived(1, 4, 6);

        await Template.SendBodyAsync("direct:start", 1);
        await Template.SendBodyAsync("direct:start", 2);
        time.Advance(TimeSpan.FromMilliseconds(999));
        await Template.SendBodyAsync("direct:start", 3);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await Template.SendBodyAsync("direct:start", 4);
        await Template.SendBodyAsync("direct:start", 5);
        time.Advance(TimeSpan.FromSeconds(5));
        await Template.SendBodyAsync("direct:start", 6);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Default_period_is_one_second()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await StartWithRoutes(b => b.From("direct:start").Sample().To("mock:result"));
        Mock("result").ExpectedBodiesReceived("a", "c");

        await Template.SendBodyAsync("direct:start", "a");
        time.Advance(TimeSpan.FromMilliseconds(500));
        await Template.SendBodyAsync("direct:start", "b");
        time.Advance(TimeSpan.FromMilliseconds(500));
        await Template.SendBodyAsync("direct:start", "c");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Lets_every_nth_exchange_through_by_frequency()
    {
        await StartWithRoutes(b => b.From("direct:start").Sample(messageFrequency: 3).To("mock:result"));
        Mock("result").ExpectedBodiesReceived(3, 6);

        for (var i = 1; i <= 7; i++)
        {
            await Template.SendBodyAsync("direct:start", i);
        }

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Sampled_out_exchanges_are_stopped_not_failed()
    {
        await StartWithRoutes(b => b.From("direct:start").Sample(messageFrequency: 2).To("mock:result"));

        var dropped = await Template.SendAsync("direct:start", e => e.Message.Body = 1);
        var passed = await Template.SendAsync("direct:start", e => e.Message.Body = 2);

        Assert.True(dropped.IsRouteStop);
        Assert.Null(dropped.Exception);
        Assert.False(passed.IsRouteStop);
        Assert.Equal(1, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Concurrent_senders_get_exactly_one_per_period()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await StartWithRoutes(b => b.From("direct:start").Sample(TimeSpan.FromMinutes(1)).To("mock:result"));

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => Template.SendBodyAsync("direct:start", i)))).Bounded();

        Assert.Equal(1, Mock("result").ReceivedCounter);
    }
}
