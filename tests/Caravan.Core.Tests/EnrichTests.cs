using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>enrich and pollEnrich semantics (mirrors Camel's Enricher* and PollEnricher* tests).</summary>
public class EnrichTests : ContextTestBase
{
    private static readonly IAggregationStrategy Append = AggregationStrategies.From(
        (original, resource) =>
        {
            original!.Message.Body = $"{original.Message.Body}:{resource.Message.Body}";
            return original;
        });

    [Fact]
    public async Task Enrich_merges_the_resource_reply_with_the_strategy()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Enrich("direct:resource", Append)
                .SetHeader("enriched", Expr.Property(ExchangePropertyKeys.ToEndpoint)).To("mock:result");
            b.From("direct:resource").Transform(_ => "blah");
        });
        Mock("result").ExpectedBodiesReceived("test:blah");

        await Template.SendBodyAsync("direct:start", "test");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal("direct:resource", Mock("result").ReceivedExchanges[0].Message.GetHeader("enriched"));
    }

    [Fact]
    public async Task Enrich_without_strategy_takes_the_reply_body_and_headers()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Enrich("direct:resource").To("mock:result");
            b.From("direct:resource").Transform(e => "reply to " + e.Message.Body).SetHeader("fromResource", _ => "yes");
        });
        Mock("result").ExpectedBodiesReceived("reply to x");
        Mock("result").ExpectedHeaderReceived("fromResource", "yes");
        Mock("result").ExpectedHeaderReceived("original", "kept");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "original", "kept");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_sends_a_correlated_InOut_copy_and_keeps_the_exchange_pattern()
    {
        Exchange? seen = null;
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Enrich("direct:resource").To("mock:result");
            b.From("direct:resource").Process(e => seen = e.Copy());
        });

        var sent = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.NotNull(seen);
        Assert.Equal(ExchangePattern.InOut, seen.Pattern);
        Assert.NotEqual(sent.ExchangeId, seen.ExchangeId);
        Assert.Equal(sent.ExchangeId, seen.GetProperty(ExchangePropertyKeys.CorrelationId));
        Assert.Equal(ExchangePattern.InOnly, Mock("result").ReceivedExchanges[0].Pattern);
        Assert.Null(Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task Enrich_with_a_dynamic_uri()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Enrich("direct:${header.source}", Append).To("mock:result");
            b.From("direct:a").Transform(_ => "A");
            b.From("direct:b").Transform(_ => "B");
        });
        Mock("result").ExpectedBodiesReceived("1:A", "2:B");

        await Template.SendBodyAndHeaderAsync("direct:start", "1", "source", "a");
        await Template.SendBodyAndHeaderAsync("direct:start", "2", "source", "b");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_failure_fails_the_exchange_unless_aggregate_on_exception()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:fail").Enrich("direct:boom", Append).To("mock:result");
            b.From("direct:aggregate").Enrich(
                "direct:boom",
                AggregationStrategies.From((original, resource) =>
                {
                    original!.Message.Body = "handled " + resource.Exception!.Message;
                    return original;
                }),
                aggregateOnException: true).To("mock:result");
            b.From("direct:boom").ThrowException(new InvalidOperationException("boom"));
        });
        Mock("result").ExpectedBodiesReceived("handled boom");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:fail", "x"));
        await Template.SendBodyAsync("direct:aggregate", "x");

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_strategy_exception_fails_with_an_aggregation_error()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Enrich("direct:resource", AggregationStrategies.From((_, _) => throw new FormatException("bad"))).To("mock:result");
            b.From("direct:resource").Transform(_ => "r");
        });
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        var aggregation = Assert.IsType<CaravanExchangeException>(ex.InnerException);
        Assert.StartsWith("Error occurred during aggregation", aggregation.Message);
        Assert.IsType<FormatException>(aggregation.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_ignores_invalid_endpoints_only_when_asked()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").Enrich(Expr.Header("uri")).To("mock:result");
            b.From("direct:lenient").Enrich(Expr.Header("uri"), ignoreInvalidEndpoint: true).To("mock:result");
        });
        Mock("result").ExpectedBodiesReceived("unchanged");

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:strict", "x", "uri", "nosuchscheme:foo"));
        await Template.SendBodyAndHeaderAsync("direct:lenient", "unchanged", "uri", "nosuchscheme:foo");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_resource_has_its_own_unit_of_work_unless_shared()
    {
        var own = new TestHelpers.RecordingSynchronization();
        var shared = new TestHelpers.RecordingSynchronization();
        var ownDoneBeforeResult = false;
        var sharedDoneBeforeResult = true;
        await StartWithRoutes(b =>
        {
            b.From("direct:own").Enrich("direct:resource-own").Process(_ => ownDoneBeforeResult = own.Succeeded is not null);
            b.From("direct:shared").Enrich("direct:resource-shared", shareUnitOfWork: true).Process(_ => sharedDoneBeforeResult = shared.Succeeded is not null);
            b.From("direct:resource-own").Process(e => e.AddOnCompletion(own));
            b.From("direct:resource-shared").Process(e => e.AddOnCompletion(shared));
        });

        await Template.SendBodyAsync("direct:own", "x");
        await Template.SendBodyAsync("direct:shared", "x");

        Assert.True(ownDoneBeforeResult);
        Assert.False(sharedDoneBeforeResult);
        Assert.True(shared.Succeeded);
    }

    [Fact]
    public async Task Poll_enrich_takes_a_waiting_message()
    {
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:resource", timeout: TimeSpan.Zero, aggregationStrategy: Append)
            .SetHeader("enriched", Expr.Property(ExchangePropertyKeys.ToEndpoint)).To("mock:result"));
        Mock("result").ExpectedBodiesReceived("x:polled");

        await Template.SendBodyAsync("seda:resource", "polled");
        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal("seda:resource", Mock("result").ReceivedExchanges[0].Message.GetHeader("enriched"));
    }

    [Fact]
    public async Task Poll_enrich_no_wait_without_message_sets_a_null_body_and_keeps_headers()
    {
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:empty", timeout: TimeSpan.Zero).To("mock:result"));
        Mock("result").ExpectedBodiesReceived([null]);
        Mock("result").ExpectedHeaderReceived("h", 1);

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "h", 1);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Poll_enrich_without_message_calls_the_strategy_no_message_hook()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:default").PollEnrich("seda:empty", TimeSpan.Zero, Append).To("mock:default");
            b.From("direct:custom").PollEnrich("seda:empty", TimeSpan.Zero, new NoMessageStrategy()).To("mock:custom");
        });
        Mock("default").ExpectedBodiesReceived("unchanged");
        Mock("custom").ExpectedBodiesReceived("nothing for x");

        await Template.SendBodyAsync("direct:default", "unchanged");
        await Template.SendBodyAsync("direct:custom", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Poll_enrich_with_timeout_waits_on_the_context_clock()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:resource", timeout: TimeSpan.FromSeconds(5)).To("mock:result"));
        Mock("result").ExpectedBodiesReceived(null, "arrived");

        var timers = time.TimersCreated;
        var first = Template.SendBodyAsync("direct:start", "x");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(first.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await first.Bounded();

        timers = time.TimersCreated;
        var second = Template.SendBodyAsync("direct:start", "y");
        await time.WaitForTimersAsync(timers + 1);
        await Template.SendBodyAsync("seda:resource", "arrived");
        await second.Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Poll_enrich_blocks_until_a_message_arrives_by_default()
    {
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:resource").To("mock:result"));
        Mock("result").ExpectedBodiesReceived("late");

        var pending = Template.SendBodyAsync("direct:start", "x");
        await Task.Yield();
        Assert.False(pending.IsCompleted);
        await Template.SendBodyAsync("seda:resource", "late");
        await pending.Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Poll_enrich_blocking_is_cancelled_with_the_exchange()
    {
        using var cancel = new CancellationTokenSource();
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:never").To("mock:result"));
        ExpectNone("result");

        var pending = Template.SendAsync("direct:start", new Exchange(Context) { CancellationToken = cancel.Token });
        await cancel.CancelAsync();
        var result = await pending.Bounded();

        var error = Assert.IsType<CaravanExchangeException>(result.Exception);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Poll_enrich_with_a_dynamic_uri_and_hands_over_completions()
    {
        var sync = new TestHelpers.RecordingSynchronization();
        await StartWithRoutes(b => b.From("direct:start").PollEnrich("seda:${header.queue}", TimeSpan.Zero).To("mock:result"));
        var queued = new Exchange(Context);
        queued.Message.Body = "from b";
        queued.AddOnCompletion(sync);
        await Template.SendAsync("seda:b", queued);
        Mock("result").ExpectedBodiesReceived("from b");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "queue", "b");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.True(await sync.Completed.Task.Bounded() is not null);
        Assert.True(sync.Succeeded);
    }

    private sealed class NoMessageStrategy : IAggregationStrategy
    {
        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange) => newExchange;

        public Exchange? AggregateNoMessage(Exchange oldExchange)
        {
            oldExchange.Message.Body = "nothing for " + oldExchange.Message.Body;
            return oldExchange;
        }
    }
}
