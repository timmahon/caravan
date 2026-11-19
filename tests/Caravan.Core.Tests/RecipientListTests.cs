using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Processors;

namespace Caravan.Core.Tests;

/// <summary>recipientList semantics (mirrors Camel's RecipientList* tests).</summary>
public class RecipientListTests : ContextTestBase
{
    [Fact]
    public async Task Sends_a_copy_to_each_recipient_of_a_comma_separated_header()
    {
        await StartWithRoutes(b => b.From("direct:start").RecipientList(Expr.Header("recipients")).To("mock:result"));
        Mock("a").ExpectedBodiesReceived("x");
        Mock("b").ExpectedBodiesReceived("x");
        Mock("c").ExpectedBodiesReceived("x");
        Mock("result").ExpectedBodiesReceived("x");

        var sent = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "x";
            e.Message.SetHeader("recipients", "mock:a, mock:b,mock:c");
        });

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var c = Mock("c").ReceivedExchanges[0];
        Assert.Equal(Mock("c").EndpointUri, c.GetProperty(ExchangePropertyKeys.RecipientListEndpoint));
        Assert.Equal(2, c.GetProperty(ExchangePropertyKeys.MulticastIndex));
        Assert.Equal(true, c.GetProperty(ExchangePropertyKeys.MulticastComplete));
        Assert.Equal(sent.ExchangeId, c.GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task Recipients_can_be_a_sequence_endpoints_or_use_another_delimiter()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:list").RecipientList(_ => new List<string> { "mock:a", "mock:b" });
            b.From("direct:endpoint").RecipientList(e => e.Context.GetEndpoint("mock:c"));
            b.From("direct:semicolon").RecipientList(Expr.Body(), delimiter: ";");
        });
        Mock("a").ExpectedMessageCount(2);
        Mock("b").ExpectedMessageCount(2);
        Mock("c").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:list", "x");
        await Template.SendBodyAsync("direct:endpoint", "x");
        await Template.SendBodyAsync("direct:semicolon", "mock:a;mock:b");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Uri_template_overload_resolves_tokens()
    {
        await StartWithRoutes(b => b.From("direct:start").RecipientList("mock:a,mock:${header.other}"));
        Mock("a").ExpectedMessageCount(1);
        Mock("z").ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "other", "z");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Default_result_is_the_reply_of_the_last_recipient()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RecipientList(Expr.Header("to")).To("mock:result");
            b.From("direct:a").Transform(_ => "from a");
            b.From("direct:b").Transform(_ => "from b");
        });
        Mock("result").ExpectedBodiesReceived("from b");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "to", "direct:a,direct:b");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Invalid_endpoint_fails_before_anything_is_sent_unless_ignored()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").RecipientList(Expr.Header("to")).To("mock:after-strict");
            b.From("direct:lenient").RecipientList(Expr.Header("to"), ignoreInvalidEndpoints: true).To("mock:after-lenient");
        });
        ExpectNone("after-strict");
        Mock("a").ExpectedMessageCount(1);
        Mock("after-lenient").ExpectedMessageCount(1);

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:strict", "x", "to", "mock:a,nosuchscheme:foo"));
        Assert.IsType<NoSuchEndpointException>(ex.InnerException);
        Assert.Equal(0, Mock("a").ReceivedCounter);
        await Template.SendBodyAndHeaderAsync("direct:lenient", "x", "to", "mock:a,nosuchscheme:foo");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task No_recipients_is_a_no_op()
    {
        await StartWithRoutes(b => b.From("direct:start").RecipientList(Expr.Header("to")).To("mock:result"));
        Mock("result").ExpectedBodiesReceived("x", "y");

        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAndHeaderAsync("direct:start", "y", "to", " , ");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Parallel_recipients_run_concurrently_and_aggregate_in_recipient_order()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RecipientList(Expr.Header("to"), parallelProcessing: true, aggregationStrategy: AggregationStrategies.String("+"));
            foreach (var name in new[] { "a", "b", "c" })
            {
                b.From("direct:" + name).Process(_ => probe.EnterAsync()).Transform(_ => name);
            }
        });

        var reply = Template.RequestBodyAndHeaderAsync<string>("direct:start", "x", "to", "direct:a,direct:b,direct:c");
        await probe.WaitForStartedAsync(3);
        probe.Release(3);

        Assert.Equal("a+b+c", await reply.Bounded());
        Assert.Equal(3, probe.Max);
    }

    [Fact]
    public async Task Failing_recipient_is_redelivered_on_its_own()
    {
        var attempts = new Dictionary<string, int>();
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:start").RecipientList(Expr.Header("to")).To("mock:result");
            b.From("direct:ok").Process(_ => attempts["ok"] = attempts.GetValueOrDefault("ok") + 1);
            b.From("direct:bad").ErrorHandler(b.NoErrorHandler()).Process(_ =>
            {
                attempts["bad"] = attempts.GetValueOrDefault("bad") + 1;
                throw new InvalidOperationException("bad");
            });
        });
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:start", "x", "to", "direct:ok,direct:bad"));

        Assert.Equal("bad", ex.InnerException!.Message);
        Assert.Equal(1, attempts["ok"]);
        Assert.Equal(3, attempts["bad"]);
    }

    [Fact]
    public async Task Stop_on_exception_skips_the_remaining_recipients()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RecipientList(Expr.Header("to"), stopOnException: true);
            b.From("direct:bad").ThrowException(new InvalidOperationException("bad"));
        });
        ExpectNone("after");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("to", "mock:a,direct:bad,mock:after"));

        Assert.IsType<CaravanExchangeException>(exchange.Exception);
        Assert.Equal(1, Mock("a").ReceivedCounter);
        await Mock("after").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Producers_are_cached_up_to_cache_size_and_stopped_with_the_route()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").RecipientList(Expr.Body(), cacheSize: 2));

        await Template.SendBodyAsync("direct:start", "mock:a,mock:b");
        await Template.SendBodyAsync("direct:start", "mock:c,mock:a");

        var processor = ((Route)Context.GetRoute("r")!).RouteContext.Services.OfType<RecipientListProcessor>().Single();
        Assert.Equal(2, processor.ProducerCache.Count);
        Assert.Equal(2, Mock("a").ReceivedCounter);

        await Context.StopRouteAsync("r");

        Assert.Equal(0, processor.ProducerCache.Count);
    }

    [Fact]
    public async Task Producer_cache_can_be_disabled()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").RecipientList(Expr.Body(), cacheSize: -1));
        Mock("a").ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:start", "mock:a");
        await Template.SendBodyAsync("direct:start", "mock:a");

        await Mock("a").AssertIsSatisfiedAsync();
        Assert.Equal(0, ((Route)Context.GetRoute("r")!).RouteContext.Services.OfType<RecipientListProcessor>().Single().ProducerCache.Count);
    }
}
