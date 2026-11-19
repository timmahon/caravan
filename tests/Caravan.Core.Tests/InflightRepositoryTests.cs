using Caravan.Builder;
using Caravan.Impl;
using static Caravan.Core.Tests.TestHelpers;

namespace Caravan.Core.Tests;

public class InflightRepositoryTests : ContextTestBase
{
    [Fact]
    public async Task Tracks_exchanges_per_route_and_browses_them()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b =>
        {
            b.From("direct:outer").RouteId("outer").To("direct:inner");
            b.From("direct:inner").RouteId("inner").Process(async _ =>
            {
                entered.SetResult();
                await gate.Task;
            });
        });
        var inflight = Context.InflightRepository;
        Assert.Equal(0, inflight.Size);

        var send = Template.SendBodyAsync("direct:outer", "x");
        await entered.Task.Bounded();
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(1, inflight.Size);
        Assert.Equal(1, inflight.GetSize("outer"));
        Assert.Equal(1, inflight.GetSize("inner"));
        Assert.Equal(1, Context.GetRoute("inner")!.InflightCount);
        var info = Assert.Single(inflight.Browse());
        Assert.Equal("inner", info.RouteId);
        Assert.Equal("outer", info.FromRouteId);
        Assert.Equal(TimeSpan.FromSeconds(5), info.Duration);
        Assert.Single(inflight.Browse("outer"));
        Assert.Empty(inflight.Browse("other"));

        gate.SetResult();
        await send.Bounded();

        Assert.Equal(0, inflight.Size);
        Assert.Equal(0, inflight.GetSize("outer"));
        Assert.Empty(inflight.Browse());
    }

    [Fact]
    public void Browse_sorts_by_duration_and_limits()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var repository = new DefaultInflightRepository();
        var older = new Exchange(Context);
        time.Advance(TimeSpan.FromSeconds(3));
        var newer = new Exchange(Context);
        repository.Add(newer, "r");
        repository.Add(older, "r");

        var sorted = repository.Browse(sortByLongestDuration: true);
        Assert.Equal([older, newer], sorted.Select(i => i.Exchange));
        Assert.Equal(TimeSpan.FromSeconds(3), sorted[0].Duration);
        Assert.Same(older, Assert.Single(repository.Browse(limit: 1, sortByLongestDuration: true)).Exchange);

        repository.Remove(older, "r");
        Assert.Equal(1, repository.Size);
        Assert.Equal(1, repository.GetSize("r"));
    }

    [Fact]
    public async Task Concurrent_exchanges_are_counted()
    {
        var gate = NewSignal();
        var entered = 0;
        var allIn = NewSignal();
        await StartWithRoutes(b => b.From("seda:in?concurrentConsumers=4").RouteId("r").Process(async _ =>
        {
            if (Interlocked.Increment(ref entered) == 4)
            {
                allIn.SetResult();
            }

            await gate.Task;
        }).To("mock:done"));
        for (var i = 0; i < 4; i++)
        {
            await Template.SendBodyAsync("seda:in", i);
        }

        await allIn.Task.Bounded();
        Assert.Equal(4, Context.InflightRepository.Size);
        Assert.Equal(4, Context.InflightRepository.GetSize("r"));

        Mock("done").ExpectedMessageCount(4);
        gate.SetResult();
        await Mock("done").AssertIsSatisfiedAsync();
        await Context.StopAsync().Bounded();
        Assert.Equal(0, Context.InflightRepository.Size);
    }
}
