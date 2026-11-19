using Caravan.Builder;

namespace Caravan.Testing.Tests;

/// <summary>NotifyBuilder (mirrors Camel's NotifyBuilderTest).</summary>
public class NotifyBuilderTests : ContextTestBase
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("seda:work");
            b.From("seda:work?concurrentConsumers=4").RouteId("work")
                .Choice(c => c.When(e => e.Message.GetBody<string>() == "fail", r => r.ThrowException(new InvalidOperationException("failed"))))
                .Transform(e => e.Message.GetBody<string>()!.ToUpperInvariant())
                .To("mock:done");
            b.From("direct:split").RouteId("split")
                .Split(Expr.Body(), s => s.To("seda:work"), parallelProcessing: true);
            b.From("direct:vip").RouteId("vip").To("mock:vip");
        });
    }

    [Fact]
    public async Task WhenDone_counts_exchanges_of_seda_routes_with_concurrent_consumers()
    {
        using var notify = new NotifyBuilder(Context).FromRoute("work").WhenDone(10).Create();

        for (var i = 0; i < 10; i++)
        {
            await Template.SendBodyAsync("seda:work", "m" + i);
        }

        Assert.True(await notify.MatchesAsync(Wait));
        Assert.Equal(10, Mock("done").ReceivedCounter);
    }

    [Fact]
    public async Task MatchesAsync_returns_false_when_the_condition_is_not_reached_in_time()
    {
        using var notify = new NotifyBuilder(Context).WhenDone(5).Create();

        await Template.SendBodyAsync("direct:vip", "x");

        Assert.False(await notify.MatchesAsync(Short));
        Assert.False(notify.Matches());
    }

    [Fact]
    public async Task From_and_FromRoute_filter_the_exchanges()
    {
        using var fromStart = new NotifyBuilder(Context).From("direct:start").WhenDone(2).Create();
        using var fromSeda = new NotifyBuilder(Context).From("seda:*").WhenDone(2).Create();
        using var fromVip = new NotifyBuilder(Context).FromRoute("v*").WhenDone(1).Create();
        using var allRoutes = new NotifyBuilder(Context).WhenDone(4).Create();

        await Template.SendBodyAsync("direct:start", "a");
        await Template.SendBodyAsync("direct:start", "b");

        Assert.True(await fromStart.MatchesAsync(Wait));
        Assert.True(await fromSeda.MatchesAsync(Wait));
        Assert.True(await allRoutes.MatchesAsync(Wait));
        Assert.False(fromVip.Matches());
    }

    [Fact]
    public async Task WhenCompleted_and_WhenFailed_count_outcomes()
    {
        using var completed = new NotifyBuilder(Context).FromRoute("work").WhenCompleted(2).Create();
        using var failed = new NotifyBuilder(Context).FromRoute("work").WhenFailed(1).Create();
        using var exactlyFailed = new NotifyBuilder(Context).FromRoute("work").WhenExactlyFailed(1).And().WhenExactlyCompleted(2).Create();

        await Template.SendBodyAsync("seda:work", "ok1");
        await Template.SendBodyAsync("seda:work", "fail");
        await Template.SendBodyAsync("seda:work", "ok2");

        Assert.True(await completed.MatchesAsync(Wait));
        Assert.True(await failed.MatchesAsync(Wait));
        Assert.True(await exactlyFailed.MatchesAsync(Wait));
    }

    [Fact]
    public async Task WhenExactlyDone_no_longer_matches_after_more_exchanges()
    {
        using var notify = new NotifyBuilder(Context).FromRoute("vip").WhenExactlyDone(2).Create();

        await Template.SendBodyAsync("direct:vip", "1");
        Assert.False(notify.Matches());
        await Template.SendBodyAsync("direct:vip", "2");
        Assert.True(await notify.MatchesAsync(Wait));
        await Template.SendBodyAsync("direct:vip", "3");

        Assert.False(notify.Matches());
    }

    [Fact]
    public async Task WhenBodiesReceived_and_WhenBodiesDone()
    {
        using var received = new NotifyBuilder(Context).FromRoute("vip").WhenBodiesReceived("a", "b").Create();
        using var exact = new NotifyBuilder(Context).FromRoute("vip").WhenExactBodiesReceived("a", "b").Create();
        using var wrongOrder = new NotifyBuilder(Context).FromRoute("vip").WhenBodiesReceived("b", "a").Create();
        using var done = new NotifyBuilder(Context).FromRoute("work").WhenBodiesDone("X").Create();

        await Template.SendBodyAsync("direct:vip", "a");
        await Template.SendBodyAsync("direct:vip", "b");
        Assert.True(exact.Matches());
        await Template.SendBodyAsync("direct:vip", "c");
        await Template.SendBodyAsync("seda:work", "x");

        Assert.True(received.Matches());
        Assert.False(exact.Matches());
        Assert.False(wrongOrder.Matches());
        Assert.True(await done.MatchesAsync(Wait));
    }

    [Fact]
    public async Task WhenAnyReceivedMatches_and_WhenAllReceivedMatches()
    {
        using var any = new NotifyBuilder(Context).FromRoute("vip").WhenAnyReceivedMatches(e => e.Message.GetBody<string>() == "gold").Create();
        using var all = new NotifyBuilder(Context).FromRoute("vip").WhenAllReceivedMatches(Expr.Body().StartsWith("g")).Create();
        using var allDone = new NotifyBuilder(Context).FromRoute("work").WhenAllDoneMatches(e => e.Message.GetBody<string>() == "UP").Create();
        using var anyDone = new NotifyBuilder(Context).FromRoute("work").WhenAnyDoneMatches(e => e.Message.GetBody<string>() == "UP").Create();

        Assert.False(all.Matches());
        await Template.SendBodyAsync("direct:vip", "gold");
        Assert.True(any.Matches());
        Assert.True(all.Matches());
        await Template.SendBodyAsync("direct:vip", "silver");
        await Template.SendBodyAsync("seda:work", "up");

        Assert.True(any.Matches());
        Assert.False(all.Matches());
        Assert.True(await allDone.MatchesAsync(Wait));
        Assert.True(await anyDone.MatchesAsync(Wait));
    }

    [Fact]
    public async Task Filter_and_WereSentTo_restrict_the_counted_exchanges()
    {
        using var filtered = new NotifyBuilder(Context).Filter(e => e.Message.GetBody<string>() == "b").WhenDone(1).Create();
        using var sentToVip = new NotifyBuilder(Context).WhenDone(2).WereSentTo("mock:vip").Create();
        using var sentToDone = new NotifyBuilder(Context).WereSentTo("mock:d*").WhenExactlyDone(1).Create();

        await Template.SendBodyAsync("direct:vip", "a");
        Assert.False(filtered.Matches());
        Assert.False(sentToVip.Matches());
        await Template.SendBodyAsync("direct:vip", "b");
        await Template.SendBodyAsync("seda:work", "c");

        Assert.True(filtered.Matches());
        Assert.True(sentToVip.Matches());
        Assert.True(await sentToDone.MatchesAsync(Wait));
    }

    [Fact]
    public async Task And_Or_Not_combine_groups()
    {
        using var and = new NotifyBuilder(Context).FromRoute("vip").WhenDone(1).And().FromRoute("work").WhenDone(1).Create();
        using var or = new NotifyBuilder(Context).FromRoute("vip").WhenDone(5).Or().FromRoute("work").WhenDone(1).Create();
        using var not = new NotifyBuilder(Context).FromRoute("work").WhenDone(1).Not().FromRoute("work").WhenFailed(1).Create();
        using var notFailed = new NotifyBuilder(Context).FromRoute("work").WhenDone(2).Not().FromRoute("work").WhenFailed(1).Create();

        await Template.SendBodyAsync("seda:work", "ok");
        Assert.True(await or.MatchesAsync(Wait));
        Assert.True(await not.MatchesAsync(Wait));
        Assert.False(and.Matches());

        await Template.SendBodyAsync("direct:vip", "x");
        await Template.SendBodyAsync("seda:work", "fail");

        Assert.True(and.Matches());
        Assert.False(await notFailed.MatchesAsync(Short));
    }

    [Fact]
    public async Task Counts_the_parts_of_a_parallel_split_handed_to_seda()
    {
        var parts = Enumerable.Range(0, 20).Select(i => "p" + i).ToList();
        using var notify = new NotifyBuilder(Context).From("seda:work").WhenDone(20).And().FromRoute("split").WhenDone(1).Create();

        await Template.SendBodyAsync("direct:split", parts);

        Assert.True(await notify.MatchesAsync(Wait));
        Assert.Equal(20, Mock("done").ReceivedCounter);
    }

    [Fact]
    public async Task Reset_forgets_what_was_seen()
    {
        using var notify = new NotifyBuilder(Context).FromRoute("vip").WhenDone(1).Create();
        await Template.SendBodyAsync("direct:vip", "x");
        Assert.True(notify.Matches());

        notify.Reset();
        Assert.False(notify.Matches());
        await Template.SendBodyAsync("direct:vip", "y");

        Assert.True(await notify.MatchesAsync(Wait));
    }

    [Fact]
    public async Task Disposed_builder_stops_listening()
    {
        var notify = new NotifyBuilder(Context).FromRoute("vip").WhenDone(1).Create();
        notify.Dispose();

        await Template.SendBodyAsync("direct:vip", "x");

        Assert.False(notify.Matches());
    }

    [Fact]
    public void Must_be_created_with_conditions()
    {
        var notCreated = new NotifyBuilder(Context).WhenDone(1);
        Assert.Throws<InvalidOperationException>(() => notCreated.Matches());
        Assert.Throws<InvalidOperationException>(() => new NotifyBuilder(Context).From("direct:start").Create());
        Assert.Throws<InvalidOperationException>(() => new NotifyBuilder(Context).WhenDone(1).And().Create());

        using var created = new NotifyBuilder(Context).WhenDone(1).Create();
        Assert.Throws<InvalidOperationException>(() => created.WhenDone(2));
        Assert.Equal("fromRoute(x).whenDone(1) or whenFailed(2)", new NotifyBuilder(Context).FromRoute("x").WhenDone(1).Or().WhenFailed(2).ToString());
    }

    [Fact]
    public async Task Adding_builders_while_exchanges_are_routed_is_safe()
    {
        var sending = Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                await Template.SendBodyAsync("seda:work", "m" + i);
            }
        });
        var builders = new List<NotifyBuilder>();
        for (var i = 0; i < 50; i++)
        {
            builders.Add(new NotifyBuilder(Context).FromRoute("work").WhenDone(1).Create());
            await Task.Yield();
        }

        await sending.WaitAsync(Wait);
        using var all = new NotifyBuilder(Context).WhenDone(1).Create();
        await Template.SendBodyAsync("seda:work", "last");
        Assert.True(await all.MatchesAsync(Wait));
        foreach (var builder in builders)
        {
            builder.Dispose();
        }
    }
}
