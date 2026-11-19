using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>routingSlip and dynamicRouter semantics (mirrors Camel's RoutingSlip* and DynamicRouter* tests).</summary>
public class RoutingSlipTests : ContextTestBase
{
    [Fact]
    public async Task Routing_slip_sends_through_each_endpoint_in_order_as_a_pipeline()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RoutingSlip(Expr.Header("slip")).To("mock:result");
            b.From("direct:a").Transform(e => e.Message.Body + "-a");
            b.From("direct:b").Transform(e => e.Message.Body + "-b").To("mock:b");
        });
        Mock("b").ExpectedBodiesReceived("x-a-b");
        Mock("result").ExpectedBodiesReceived("x-a-b");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "slip", "direct:a, direct:b");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal("direct:b", Mock("b").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.SlipEndpoint));
    }

    [Fact]
    public async Task Routing_slip_string_is_a_simple_expression()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RoutingSlip("direct:a,mock:${header.target}");
            b.From("direct:a").Transform(e => e.Message.Body + "-a");
        });
        Mock("last").ExpectedBodiesReceived("x-a");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "target", "last");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Routing_slip_returns_the_reply_of_the_last_step()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RoutingSlip(_ => new[] { "direct:a", "direct:b" });
            b.From("direct:a").Transform(e => e.Message.Body + "A");
            b.From("direct:b").Transform(e => e.Message.Body + "B");
        });

        Assert.Equal("xAB", await Template.RequestBodyAsync("direct:start", "x"));
    }

    [Fact]
    public async Task Routing_slip_with_custom_delimiter_and_endpoint_instances()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:delim").RoutingSlip(Expr.Header("slip"), uriDelimiter: "#");
            b.From("direct:endpoint").RoutingSlip(e => e.Context.GetEndpoint("mock:c"));
        });
        Mock("a").ExpectedMessageCount(1);
        Mock("b").ExpectedMessageCount(1);
        Mock("c").ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:delim", "x", "slip", "mock:a#mock:b");
        await Template.SendBodyAsync("direct:endpoint", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Routing_slip_with_no_endpoints_continues_routing()
    {
        await StartWithRoutes(b => b.From("direct:start").RoutingSlip(Expr.Header("slip")).To("mock:result"));
        Mock("result").ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:start", "no header");
        await Template.SendBodyAndHeaderAsync("direct:start", "empty", "slip", " ");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Routing_slip_stops_at_the_first_failing_step()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RoutingSlip(Expr.Header("slip")).To("mock:result");
            b.From("direct:fail").ThrowException(new InvalidOperationException("boom"));
        });
        ExpectNone("after");
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(
            () => Template.SendBodyAndHeaderAsync("direct:start", "x", "slip", "mock:before,direct:fail,mock:after"));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(1, Mock("before").ReceivedCounter);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Routing_slip_redelivers_only_the_failing_step()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:start").RoutingSlip(Expr.Header("slip")).To("mock:result");
        });
        Mock("flaky").WhenAnyExchangeReceived(_ =>
        {
            if (++attempts < 3)
            {
                throw new InvalidOperationException("flaky");
            }
        });
        Mock("first").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "slip", "mock:first,mock:flaky");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Routing_slip_ignores_invalid_endpoints_only_when_asked()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").RoutingSlip(Expr.Header("slip"));
            b.From("direct:lenient").RoutingSlip(Expr.Header("slip"), ignoreInvalidEndpoints: true).To("mock:result");
        });
        Mock("a").ExpectedMessageCount(1);
        Mock("b").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Assert.ThrowsAsync<CaravanExecutionException>(
            () => Template.SendBodyAndHeaderAsync("direct:strict", "x", "slip", "nosuchscheme:foo,mock:never"));
        await Template.SendBodyAndHeaderAsync("direct:lenient", "x", "slip", "mock:a,nosuchscheme:foo,mock:b");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(0, Mock("never").ReceivedCounter);
    }

    [Fact]
    public async Task Stop_in_a_slip_step_stops_the_exchange()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").RoutingSlip(Expr.Header("slip")).To("mock:result");
            b.From("direct:stop").Stop();
        });
        ExpectNone("result");
        ExpectNone("after");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "slip", "direct:stop,mock:after");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Dynamic_router_evaluates_after_each_step_until_null()
    {
        var seen = new List<object?>();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").DynamicRouter(e =>
            {
                seen.Add(e.GetProperty(ExchangePropertyKeys.SlipEndpoint));
                return e.GetProperty<int>(ExchangePropertyKeys.DynamicRouterIndex) switch
                {
                    0 => "mock:a",
                    1 => "direct:b",
                    2 => "mock:c",
                    _ => null,
                };
            }).To("mock:result");
            b.From("direct:b").Transform(e => e.Message.Body + "-b");
        });
        Mock("a").ExpectedBodiesReceived("x");
        Mock("c").ExpectedBodiesReceived("x-b");
        Mock("result").ExpectedBodiesReceived("x-b");

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal([null, "mock:a", "direct:b", "mock:c"], seen);
    }

    [Fact]
    public async Task Dynamic_router_may_return_several_endpoints_per_step()
    {
        var calls = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .DynamicRouter(_ => ++calls == 1 ? "mock:a,mock:b" : null)
            .To("mock:result"));
        Mock("a").ExpectedMessageCount(1);
        Mock("b").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Dynamic_router_ends_on_an_empty_result_and_on_failure()
    {
        var calls = 0;
        await StartWithRoutes(b =>
        {
            b.From("direct:empty").DynamicRouter(_ => ++calls == 1 ? "mock:a" : "").To("mock:result");
            b.From("direct:fail").DynamicRouter(_ => "direct:boom");
            b.From("direct:boom").ThrowException(new InvalidOperationException("boom"));
        });
        Mock("a").ExpectedMessageCount(1);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:empty", "x");
        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:fail", "x"));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Dynamic_router_that_never_ends_is_stopped_by_cancellation()
    {
        using var cancel = new CancellationTokenSource();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").DynamicRouter(_ => "direct:step");
            b.From("direct:step").Process(e =>
            {
                if (e.GetProperty<int>(ExchangePropertyKeys.DynamicRouterIndex) == 100)
                {
                    cancel.Cancel();
                }
            });
        });

        var exchange = new Exchange(Context) { CancellationToken = cancel.Token };
        var result = await Template.SendAsync("direct:start", exchange);

        Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
        Assert.Equal(100, result.GetProperty<int>(ExchangePropertyKeys.DynamicRouterIndex));
    }
}
